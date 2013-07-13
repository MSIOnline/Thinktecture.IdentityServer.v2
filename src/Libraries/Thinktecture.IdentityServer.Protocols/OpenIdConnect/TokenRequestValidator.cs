﻿using System;
using System.Security.Claims;
using Thinktecture.IdentityModel.Constants;
using Thinktecture.IdentityServer.Models;
using Thinktecture.IdentityServer.Protocols.OAuth2;
using Thinktecture.IdentityServer.Repositories;

namespace Thinktecture.IdentityServer.Protocols.OpenIdConnect
{
    class TokenRequestValidator
    {
        public IClientsRepository Clients { get; set; }
        public IGrantsRepository Grants { get; set; }

        public TokenRequestValidator(IClientsRepository clients, IGrantsRepository grants)
        {
            Clients = clients;
            Grants = grants;
        }

        public ValidatedRequest Validate(TokenRequest request, ClaimsPrincipal clientPrincipal)
        {
            var validatedRequest = new ValidatedRequest();

            // validate request model binding
            if (request == null)
            {
                throw new TokenRequestValidationException(
                    "Invalid request parameters.",
                    OAuth2Constants.Errors.InvalidRequest);
            }

            // grant type is required
            if (string.IsNullOrWhiteSpace(request.Grant_Type))
            {
                throw new TokenRequestValidationException(
                    "Missing grant_type",
                    OAuth2Constants.Errors.UnsupportedGrantType);
            }

            // check supported grant types
            if (request.Grant_Type == OAuth2Constants.GrantTypes.AuthorizationCode ||
                request.Grant_Type == OAuth2Constants.GrantTypes.RefreshToken)
            {
                validatedRequest.GrantType = request.Grant_Type;
                Tracing.Information("Grant type: " + validatedRequest.GrantType);
            }
            else
            {
                throw new TokenRequestValidationException(
                    "Invalid grant_type: " + request.Grant_Type,
                    OAuth2Constants.Errors.UnsupportedGrantType);
            }

            // validate client credentials
            var client = ValidateClient(clientPrincipal);
            if (client == null)
            {
                throw new TokenRequestValidationException(
                    "Invalid client: " + clientPrincipal.Identity.Name,
                    OAuth2Constants.Errors.InvalidClient);
            }

            validatedRequest.Client = client;
            Tracing.InformationFormat("Client: {0} ({1})",
                validatedRequest.Client.Name,
                validatedRequest.Client.ClientId);

            switch (request.Grant_Type)
            {
                case OAuth2Constants.GrantTypes.AuthorizationCode:
                    ValidateCodeGrant(validatedRequest, request);
                    break;
                case OAuth2Constants.GrantTypes.RefreshToken:
                    ValidateRefreshTokenGrant(validatedRequest, request);
                    break;
                default:
                    throw new TokenRequestValidationException(
                        "Invalid grant_type: " + request.Grant_Type,
                        OAuth2Constants.Errors.UnsupportedGrantType);
            }

            Tracing.Information("Token request validation successful.");
            return validatedRequest;
        }

        private Client ValidateClient(ClaimsPrincipal clientPrincipal)
        {
            if (!clientPrincipal.Identity.IsAuthenticated)
            {
                Tracing.Error("Anonymous client.");
                return null;
            }

            var passwordClaim = ClaimsPrincipal.Current.FindFirst("password");
            if (passwordClaim == null)
            {
                Tracing.Error("No client secret provided.");
                return null;
            }

            Client client;
            if (Clients.ValidateAndGetClient(
                    clientPrincipal.Identity.Name,
                    passwordClaim.Value,
                    out client))
            {
                return client;
            }

            return null;
        }

        private void ValidateRefreshTokenGrant(ValidatedRequest validatedRequest, TokenRequest request)
        {
            throw new NotImplementedException();
        }

        private void ValidateCodeGrant(ValidatedRequest validatedRequest, TokenRequest request)
        {
            if (!validatedRequest.Client.AllowCodeFlow)
            {
                throw new TokenRequestValidationException(
                    "Code flow not allowed for client",
                    OAuth2Constants.Errors.UnauthorizedClient);
            }

            // code needs to be present
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                throw new TokenRequestValidationException(
                    "Missing authorization code",
                    OAuth2Constants.Errors.InvalidGrant);
            }

            validatedRequest.AuthorizationCode = request.Code;
            Tracing.Information("Authorization code: " + validatedRequest.AuthorizationCode);

            // check for authorization code in datastore
            var grant = Grants.Get(validatedRequest.AuthorizationCode);
            if (grant == null)
            {
                throw new TokenRequestValidationException(
                    "Authorization code not found: " + validatedRequest.AuthorizationCode,
                    OAuth2Constants.Errors.InvalidGrant);
            }

            validatedRequest.Grant = grant;
            validatedRequest.GrantsRepository = Grants;
            Tracing.Information("Token handle found: " + grant.HandleId);

            // check the client binding
            if (grant.ClientId != validatedRequest.Client.ClientId)
            {
                throw new TokenRequestValidationException(
                    string.Format("Client {0} is trying to request token using an authorization code from {1}.", validatedRequest.Client.ClientId, grant.ClientId),
                    OAuth2Constants.Errors.InvalidGrant);
            }

            // redirect URI is required
            if (string.IsNullOrWhiteSpace(request.Redirect_Uri))
            {
                throw new TokenRequestValidationException(
                    string.Format("Redirect URI is missing"),
                    OAuth2Constants.Errors.InvalidRequest);
            }

            // check if redirect URI from authorize and token request match
            if (!grant.RedirectUri.Equals(request.Redirect_Uri))
            {
                throw new TokenRequestValidationException(
                    string.Format("Redirect URI in token request ({0}), does not match redirect URI from authorize request ({1})", validatedRequest.RedirectUri, grant.RedirectUri),
                    OAuth2Constants.Errors.InvalidRequest);
            }
        }
    }
}
