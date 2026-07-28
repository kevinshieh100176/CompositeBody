using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Services.Authentication.Internal;
using Unity.Services.Authentication.Server.Internal;
using Unity.Services.Core.Analytics.Internal;
using Unity.Services.Core.Configuration.Internal;
using Unity.Services.Core.Device.Internal;
using Unity.Services.Core.Internal;
using Unity.Services.Matchmaker.Backfill;
using Unity.Services.Matchmaker.Http;
using Unity.Services.Matchmaker.Models;
using Unity.Services.Matchmaker.Overrides;
using Unity.Services.Multiplayer;
using UnityEngine;
using Logger = Unity.Services.Multiplayer.Logger;
using Player = Unity.Services.Matchmaker.Models.Player;

namespace Unity.Services.Matchmaker
{
    internal class WrappedMatchmakerService : IMatchmakerSdkConfiguration, IMatchmakerService
    {
        const string CloudEnvironmentKey = "com.unity.services.core.cloud-environment";

        internal readonly IMatchmakerServiceSdk m_MatchmakerService;
        internal readonly ICloudProjectId m_CloudProjectId;
        private readonly IABRemoteConfig m_abRemoteConfig;
        private readonly IABAnalytics m_abAnalytics;

        internal WrappedMatchmakerService(
            ICloudProjectId cloudProjectId,
            IProjectConfiguration projectConfiguration,
            IInstallationId installationId,
            IEnvironmentId environmentIdProvider,
            IMatchmakerServiceSdk matchmakerService,
            IABRemoteConfig abRemoteConfig = null,
            IABAnalytics abAnalytics = null)
        {
            m_CloudProjectId = cloudProjectId;
            m_MatchmakerService = matchmakerService;

            var projectId = m_CloudProjectId.GetCloudProjectId();
            var environmentId = environmentIdProvider?.EnvironmentId ?? "";

            if (abAnalytics == null)
            {
                if (CoreRegistry.Instance.TryGetServiceComponent<IAnalyticsStandardEventComponent>(
                    out var softAnalytics))
                {
                    m_abAnalytics = softAnalytics != null ? new ABAnalytics(projectId, environmentId, softAnalytics) : null;
                }
            }
            else
            {
                m_abAnalytics = abAnalytics;
            }

            // Having abRemoteConfig without analytics would break ABTesting
            if (m_abAnalytics == null)
                return;

            if (abRemoteConfig == null)
            {
                var userId = installationId?.GetOrCreateIdentifier();
                var cloudEnvironment = projectConfiguration?.GetString(CloudEnvironmentKey);

                m_abRemoteConfig =
                    new ABRemoteConfig(new HttpClient(), userId, cloudEnvironment, projectId, environmentId);
            }
            else
            {
                m_abRemoteConfig = abRemoteConfig;
            }
        }

        /// <summary>
        /// Sets the base path in configuration.
        /// </summary>
        /// <param name="basePath">The base path to set in configuration.</param>
        public void SetBasePath(string basePath)
        {
            m_MatchmakerService.Configuration.BasePath = basePath;
        }

        #region Wrapped Tickets API

        /// <inheritdoc/>
        /// <exception cref="System.ArgumentNullException">An exception thrown when the request is missing the minimum required number of players.</exception>
        public async Task<CreateTicketResponse> CreateTicketAsync(List<Player> players, CreateTicketOptions options)
        {
            EnsureSignedIn();

            if (players == null || players.Count < 1)
            {
                throw new ArgumentNullException(nameof(players),
                    "Cannot create a matchmaking ticket without at least 1 player to add to the queue!");
            }

            var queueName = options?.QueueName;
            var attributes = options?.Attributes;

            if (m_abRemoteConfig != null)
            {
                // Note that the refresh will only be done the first time this method is called.
                await m_abRemoteConfig.RefreshGameOverridesAsync();
            }

            var model = new CreateTicketRequest(
                players,
                queueName,
                attributes,
                m_abRemoteConfig?.Overrides);

            var request = new Tickets.CreateTicketRequest(default, model);
            var response = await TryCatchRequest(m_MatchmakerService.TicketsApi.CreateTicketAsync, request);

            var result = response.Result;

            if (result.AbTestingResult != null && result.AbTestingResult.IsAbTesting)
            {
                m_abAnalytics?.SubmitUserAssignmentConfirmedEvent(result.AbTestingResult.VariantId,
                    m_abRemoteConfig?.AssignmentId);
            }

            return result;
        }

        /// <inheritdoc/>
        /// <exception cref="System.ArgumentNullException">An exception thrown when a required parameter is null, empty, or containing only whitespace.</exception>
        public async Task DeleteTicketAsync(string ticketId)
        {
            EnsureSignedIn();
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                throw new ArgumentNullException("ticketId",
                    "Argument should be non-null, non-empty & not only whitespaces.");
            }

            var request = new Tickets.DeleteTicketRequest(ticketId);
            await TryCatchRequest(m_MatchmakerService.TicketsApi.DeleteTicketAsync, request);
        }

        /// <inheritdoc/>
        /// <exception cref="System.ArgumentNullException">An exception thrown when a required parameter is null, empty, or containing only whitespace.</exception>
        public async Task<TicketStatusResponse> GetTicketAsync(string ticketId)
        {
            EnsureSignedIn();
            if (string.IsNullOrWhiteSpace(ticketId))
            {
                throw new ArgumentNullException("ticketId",
                    "Argument should be non-null, non-empty & not only whitespaces.");
            }

            var request = new Tickets.GetTicketStatusRequest(ticketId);
            var response = await TryCatchRequest(m_MatchmakerService.TicketsApi.GetTicketStatusAsync, request);
            return response.Result;
        }

        #endregion

        #region Wrapped Backfill API

        /// <inheritdoc/>
        public async Task<BackfillTicket> ApproveBackfillTicketAsync(string backfillTicketId)
        {
            var request = new ApproveBackfillTicketRequest(backfillTicketId);
            var response = await TryCatchRequest(m_MatchmakerService.BackfillApi.ApproveBackfillTicketAsync, request);

            //Convert the legacy response to the user-facing response.
            return response.Result.GetCompatibilityModel();
        }

        /// <inheritdoc/>
        public async Task<string> CreateBackfillTicketAsync(CreateBackfillTicketOptions options)
        {
            if (options is null)
            {
                throw new ArgumentNullException($"{nameof(options)} must not be null.");
            }

            if (string.IsNullOrWhiteSpace(options.Connection) && options.ConnectionDetails is null)
            {
                throw new SessionException($"Either {nameof(options.Connection)} or {nameof(options.ConnectionDetails)} must be provided to create a backfill ticket.", SessionError.InvalidBackfillTicketOptions);
            }

            if (!string.IsNullOrWhiteSpace(options.Connection) && options.ConnectionDetails is not null)
            {
                Logger.LogWarning($"When using both {nameof(options.Connection)} and {options.ConnectionDetails}, {nameof(options.ConnectionDetails)} will take precedence.");
            }

            var request = new Backfill.CreateBackfillTicketRequest(options.GetLegacyModel());
            var response = await TryCatchRequest(m_MatchmakerService.BackfillApi.CreateBackfillTicketAsync, request);
            return response.Result.Id;
        }

        /// <inheritdoc/>
        public async Task DeleteBackfillTicketAsync(string backfillTicketId)
        {
            var request = new DeleteBackfillTicketRequest(backfillTicketId);
            await TryCatchRequest(m_MatchmakerService.BackfillApi.DeleteBackfillTicketAsync, request);
        }

        /// <inheritdoc/>
        public async Task UpdateBackfillTicketAsync(string backfillTicketId, BackfillTicket ticket)
        {
            var request = new UpdateBackfillTicketRequest(backfillTicketId, ticket.GetLegacyModel());
            await TryCatchRequest(m_MatchmakerService.BackfillApi.UpdateBackfillTicketAsync, request);
        }

        #endregion

        #region Wrapped Matches API

        /// <inheritdoc/>
        /// <exception cref="System.ArgumentNullException">An exception thrown when the request is missing the match id.</exception>
        public async Task<StoredMatchmakingResults> GetMatchmakingResultsAsync(string matchId)
        {
            EnsureSignedIn();

            if (string.IsNullOrWhiteSpace(matchId))
            {
                throw new ArgumentNullException("matchId",
                    "Argument should be non-null, non-empty & not only whitespaces.");
            }

            var request = new Matches.GetMatchmakingResultsRequest(matchId, m_CloudProjectId.GetCloudProjectId());
            var response = await TryCatchRequest(m_MatchmakerService.MatchesApi.GetMatchmakingResultsAsync, request);

            var result = response.Result;
            return result;
        }

        #endregion

        #region Helper Functions

        // Helper function to reduce code duplication of try-catch
        private async Task<Response> TryCatchRequest<TRequest>(Func<TRequest, Configuration, Task<Response>> func,
            TRequest request)
        {
            Response response = null;
            try
            {
                response = await func(request, m_MatchmakerService.Configuration);
            }
            catch (HttpException he)
            {
                int httpErrorStatusCode = (int)he.Response.StatusCode;
                MatchmakerExceptionReason reason = MatchmakerExceptionReason.Unknown;
                if (he.Response.IsNetworkError)
                {
                    reason = MatchmakerExceptionReason.NetworkError;
                }
                else if (he.Response.IsHttpError)
                {
                    //Elevate unhandled http codes to lobby enum range
                    if (httpErrorStatusCode < 1000)
                    {
                        httpErrorStatusCode += (int)MatchmakerExceptionReason.Min;
                        if (Enum.IsDefined(typeof(MatchmakerExceptionReason), httpErrorStatusCode))
                        {
                            reason = (MatchmakerExceptionReason)httpErrorStatusCode;
                        }
                    }
                }

                ResolveErrorWrapping(reason, he);
            }
            catch (Exception e)
            {
                ResolveErrorWrapping(MatchmakerExceptionReason.Unknown, e);
            }

            return response;
        }

        // Helper function to reduce code duplication of try-catch (with server access token)
        private async Task<Response> TryCatchRequest<TRequest>(
            Func<TRequest, string, Configuration, Task<Response>> func, TRequest request)
        {
            if (string.IsNullOrEmpty(m_MatchmakerService.ServerAccessToken?.AccessToken))
                throw new MatchmakerServiceException(MatchmakerExceptionReason.Unauthorized,
                    "Backfill operations require a server access token.");

            Response response = null;
            try
            {
                response = await func(request, m_MatchmakerService.ServerAccessToken.AccessToken,
                    m_MatchmakerService.Configuration);
            }
            catch (HttpException he)
            {
                int httpErrorStatusCode = (int)he.Response.StatusCode;
                MatchmakerExceptionReason reason = MatchmakerExceptionReason.Unknown;
                if (he.Response.IsNetworkError)
                {
                    reason = MatchmakerExceptionReason.NetworkError;
                }
                else if (he.Response.IsHttpError)
                {
                    //Elevate unhandled http codes to lobby enum range
                    if (httpErrorStatusCode < 1000)
                    {
                        httpErrorStatusCode += (int)MatchmakerExceptionReason.Min;
                        if (Enum.IsDefined(typeof(MatchmakerExceptionReason), httpErrorStatusCode))
                        {
                            reason = (MatchmakerExceptionReason)httpErrorStatusCode;
                        }
                    }
                }

                ResolveErrorWrapping(reason, he);
            }
            catch (Exception e)
            {
                ResolveErrorWrapping(MatchmakerExceptionReason.Unknown, e);
            }

            return response;
        }

        // Helper function to reduce code duplication of try-catch (generic version)
        private async Task<Response<TReturn>> TryCatchRequest<TRequest, TReturn>(
            Func<TRequest, Configuration, Task<Response<TReturn>>> func, TRequest request)
        {
            Response<TReturn> response = null;
            try
            {
                response = await func(request, m_MatchmakerService.Configuration);
            }
            catch (HttpException he)
            {
                int httpErrorStatusCode = (int)he.Response.StatusCode;
                MatchmakerExceptionReason reason = MatchmakerExceptionReason.Unknown;
                if (he.Response.IsNetworkError)
                {
                    reason = MatchmakerExceptionReason.NetworkError;
                }
                else if (he.Response.IsHttpError)
                {
                    //Elevate unhandled http codes to lobby enum range
                    if (httpErrorStatusCode < 1000)
                    {
                        httpErrorStatusCode += (int)MatchmakerExceptionReason.Min;
                        if (Enum.IsDefined(typeof(MatchmakerExceptionReason), httpErrorStatusCode))
                        {
                            reason = (MatchmakerExceptionReason)httpErrorStatusCode;
                        }
                    }
                }

                ResolveErrorWrapping(reason, he);
            }
            catch (Exception e)
            {
                ResolveErrorWrapping(MatchmakerExceptionReason.Unknown, e);
            }

            return response;
        }

        // Helper function to reduce code duplication of
        // try-catch (generic version with server access token).
        //TODO - is there a way to not have to duplicate for variable parameters?
        private async Task<Response<TReturn>> TryCatchRequest<TRequest, TReturn>(
            Func<TRequest, string, Configuration, Task<Response<TReturn>>> func, TRequest request)
        {
            if (string.IsNullOrEmpty(m_MatchmakerService.ServerAccessToken?.AccessToken))
                throw new MatchmakerServiceException(MatchmakerExceptionReason.Unauthorized,
                    "Backfill operations require a server access token.");

            Response<TReturn> response = null;
            try
            {
                response = await func(request, m_MatchmakerService.ServerAccessToken.AccessToken,
                    m_MatchmakerService.Configuration);
            }
            catch (HttpException he)
            {
                int httpErrorStatusCode = (int)he.Response.StatusCode;
                MatchmakerExceptionReason reason = MatchmakerExceptionReason.Unknown;
                if (he.Response.IsNetworkError)
                {
                    reason = MatchmakerExceptionReason.NetworkError;
                }
                else if (he.Response.IsHttpError)
                {
                    //Elevate unhandled http codes to lobby enum range
                    if (httpErrorStatusCode < 1000)
                    {
                        httpErrorStatusCode += (int)MatchmakerExceptionReason.Min;
                        if (Enum.IsDefined(typeof(MatchmakerExceptionReason), httpErrorStatusCode))
                        {
                            reason = (MatchmakerExceptionReason)httpErrorStatusCode;
                        }
                    }
                }

                ResolveErrorWrapping(reason, he);
            }
            catch (Exception e)
            {
                ResolveErrorWrapping(MatchmakerExceptionReason.Unknown, e);
            }

            return response;
        }

        // Helper function to resolve the new wrapped error/exception based on input parameter
        private void ResolveErrorWrapping(MatchmakerExceptionReason reason, Exception exception = null)
        {
            if (reason == MatchmakerExceptionReason.Unknown)
            {
                Logger.LogError(
                    $"{Enum.GetName(typeof(MatchmakerExceptionReason), reason)} ({(int)reason}). Message: Something went wrong.");
                throw new MatchmakerServiceException(reason, "Something went wrong.", exception);
            }
            else
            {
                //Check if the exception is of type HttpException<ProblemDetails> - extract api user-facing message
                HttpException<ProblemDetails> apiException = exception as HttpException<ProblemDetails>;
                if (apiException != null)
                {
                    ProblemDetails ae = apiException.ActualError;
                    if (ae != null)
                    {
                        var jsonObject = ae.Errors.GetAs<JsonObject>();
                        string errorBody = jsonObject?.obj == null
                            ? ae.Detail
                            : (Environment.NewLine + JsonConvert.SerializeObject(jsonObject));

                        //Log both details and errors as the API isn't consistent at the moment with how the errors are returned.
                        Logger.LogError($"{Enum.GetName(typeof(MatchmakerExceptionReason), reason)} ({(int)reason}) " +
                            $"{Environment.NewLine} Title: {apiException.ActualError.Title} " +
                            $"{Environment.NewLine} Errors: {errorBody}" +
                            $"{Environment.NewLine}");
                    }

                    throw new MatchmakerServiceException(reason, apiException.Response.ErrorMessage, apiException);
                }
                else
                {
                    //Other general exception message handling
                    Logger.LogError(
                        $"{Enum.GetName(typeof(MatchmakerExceptionReason), reason)} ({(int)reason}). Message: {exception.Message}");
                    throw new MatchmakerServiceException(reason, exception.Message, exception);
                }
            }
        }

        #endregion

        private void EnsureSignedIn()
        {
            if (m_MatchmakerService.AccessToken.AccessToken == null)
            {
                throw new MatchmakerServiceException(MatchmakerExceptionReason.Unauthorized,
                    "You are not signed in to the Authentication Service. Please sign in.");
            }
        }
    }

    /// <summary>
    /// Helper extension class for converting to/from Backfill v2 api data models.
    /// </summary>
    internal static class BackfillApiCompatibilityExtensions
    {
        internal static Models.CreateBackfillTicketRequest GetLegacyModel(this CreateBackfillTicketOptions options)
        {
            var legacyProperties = options.Properties.GetLegacyModel();
            return new Models.CreateBackfillTicketRequest(legacyProperties, options.QueueName, options.Connection, options.ConnectionDetails,
                options.Attributes, options.PoolId, options.MatchId);
        }

        internal static LegacyBackfillTicket GetLegacyModel(this BackfillTicket options)
        {
            var legacyProperties = options.Properties.GetLegacyModel();
            return new LegacyBackfillTicket(options.Id, options.Connection, options.Attributes, legacyProperties);
        }

        internal static Dictionary<string, byte[]> GetLegacyModel(this BackfillTicketProperties properties)
        {
            var legacyModel = new Dictionary<string, byte[]>();

            //Convert models to byte[]
            var json = JsonConvert.SerializeObject(properties);
            var dataBytes = Encoding.UTF8.GetBytes(json);
            legacyModel.Add("Data", dataBytes);

            return legacyModel;
        }

        internal static BackfillTicket GetCompatibilityModel(this LegacyBackfillTicket legacyTicket)
        {
            //Decode and deserialize properties.
            var propertiesJson = Encoding.UTF8.GetString(legacyTicket.Properties["Data"]);
            var deserialized = JsonConvert.DeserializeObject<BackfillTicketProperties>(propertiesJson);

            return new BackfillTicket(legacyTicket.Id, legacyTicket.Connection, legacyTicket.Attributes, deserialized);
        }
    }
}
