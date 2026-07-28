using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Services.Authentication.Internal;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;

namespace Unity.Services.Multiplayer
{
    internal static class LobbyConverter
    {
        const string k_MultiplayServerAttemptingToConnectAnExistingSessionErrorMessage = "only players can join lobbies";

        internal static DataObject ToSessionDataObject(SessionProperty sessionData)
        {
            return sessionData == null ? null : new DataObject((DataObject.VisibilityOptions)sessionData.Visibility, sessionData.Value, (DataObject.IndexOptions)sessionData.Index);
        }

        internal static SessionProperty ToSessionProperty(DataObject dataObject)
        {
            return dataObject == null ? null : new SessionProperty(dataObject.Value, (VisibilityPropertyOptions)dataObject.Visibility, (PropertyIndex)dataObject.Index);
        }

        internal static PlayerDataObject ToPlayerDataObject(PlayerProperty memberData)
        {
            return new PlayerDataObject((PlayerDataObject.VisibilityOptions)memberData.Visibility, memberData.Value);
        }

        internal static Lobbies.Models.Player ToLobbyPlayer(IPlayerId playerId, Dictionary<string, PlayerProperty> playerProperties)
        {
            if (playerId == null)
                return null;

            var lobbyPlayerData = playerProperties?.ToDictionary(
                kvpPlayerProperty => kvpPlayerProperty.Key,
                kvpPlayerProperty => ToPlayerDataObject(kvpPlayerProperty.Value));

            return new Lobbies.Models.Player(
                id: playerId.PlayerId,
                data: lobbyPlayerData);
        }

        internal static Lobbies.Models.Player ToLobbyPlayer(Player player)
        {
            if (player == null)
                return null;

            var lobbyPlayerData = player?.Properties?.ToDictionary(
                kvpPlayerProperty => kvpPlayerProperty.Key,
                kvpPlayerProperty => ToPlayerDataObject(kvpPlayerProperty.Value));

            return new Lobbies.Models.Player(
                player.Id,
                player.ConnectionInfo,
                lobbyPlayerData,
                player.AllocationId,
                player.Joined,
                player.LastUpdated);
        }

        internal static QueryLobbiesOptions ToQueryLobbiesOptions(QuerySessionsOptions options)
        {
            return new QueryLobbiesOptions()
            {
                Filters = ToQueryFilters(options.FilterOptions),
                Order = ToQueryOrders(options.SortOptions),
                Count = options.Count,
                Skip = options.Skip,
                ContinuationToken = options.ContinuationToken,
            };
        }

        internal static List<QueryFilter> ToQueryFilters(List<FilterOption> filterOptions)
        {
            var queryFilters = new List<QueryFilter>();

            if (filterOptions != null)
            {
                foreach (var filter in filterOptions)
                {
                    if (filter != null)
                    {
                        queryFilters.Add(ToFilterOption(filter));
                    }
                }
            }

            return queryFilters;
        }

        static List<QueryOrder> ToQueryOrders(List<SortOption> sortOptions)
        {
            var order = new List<QueryOrder>();

            if (sortOptions != null)
            {
                foreach (var option in sortOptions)
                {
                    if (option != null)
                    {
                        var asc = option.Order == SortOrder.Ascending;
                        var sortFieldOption = ToFieldOptions(option.Field);
                        order.Add(new QueryOrder(asc, sortFieldOption));
                    }
                }
            }

            return order;
        }

        static QueryFilter ToFilterOption(FilterOption option)
        {
            return new QueryFilter(ToFilterField(option.Field), option.Value, ToFilterOperator(option.Operation));
        }

        static QueryFilter.OpOptions ToFilterOperator(FilterOperation filterOperation)
        {
            switch (filterOperation)
            {
                case FilterOperation.Contains:
                    return QueryFilter.OpOptions.CONTAINS;
                case FilterOperation.Equal:
                    return QueryFilter.OpOptions.EQ;
                case FilterOperation.NotEqual:
                    return QueryFilter.OpOptions.NE;
                case FilterOperation.Less:
                    return QueryFilter.OpOptions.LT;
                case FilterOperation.LessOrEqual:
                    return QueryFilter.OpOptions.LE;
                case FilterOperation.Greater:
                    return QueryFilter.OpOptions.GT;
                case FilterOperation.GreaterOrEqual:
                    return QueryFilter.OpOptions.GE;
            }

            throw new Exception("Invalid FilterOperation");
        }

        static QueryFilter.FieldOptions ToFilterField(FilterField filterField)
        {
            switch (filterField)
            {
                case FilterField.AvailableSlots:
                    return QueryFilter.FieldOptions.AvailableSlots;
                case FilterField.Name:
                    return QueryFilter.FieldOptions.Name;
                case FilterField.Created:
                    return QueryFilter.FieldOptions.Created;
                case FilterField.LastUpdated:
                    return QueryFilter.FieldOptions.LastUpdated;
                case FilterField.StringIndex1:
                    return QueryFilter.FieldOptions.S1;
                case FilterField.StringIndex2:
                    return QueryFilter.FieldOptions.S2;
                case FilterField.StringIndex3:
                    return QueryFilter.FieldOptions.S3;
                case FilterField.StringIndex4:
                    return QueryFilter.FieldOptions.S4;
                case FilterField.StringIndex5:
                    return QueryFilter.FieldOptions.S5;
                case FilterField.NumberIndex1:
                    return QueryFilter.FieldOptions.N1;
                case FilterField.NumberIndex2:
                    return QueryFilter.FieldOptions.N2;
                case FilterField.NumberIndex3:
                    return QueryFilter.FieldOptions.N3;
                case FilterField.NumberIndex4:
                    return QueryFilter.FieldOptions.N4;
                case FilterField.NumberIndex5:
                    return QueryFilter.FieldOptions.N5;
                case FilterField.IsLocked:
                    return QueryFilter.FieldOptions.IsLocked;
                case FilterField.HasPassword:
                    return QueryFilter.FieldOptions.HasPassword;
            }

            throw new Exception("Invalid FilterField");
        }

        static QueryOrder.FieldOptions ToFieldOptions(SortField sortField)
        {
            switch (sortField)
            {
                case SortField.Name:
                    return QueryOrder.FieldOptions.Name;
                case SortField.MaxPlayers:
                    return QueryOrder.FieldOptions.MaxPlayers;
                case SortField.AvailableSlots:
                    return QueryOrder.FieldOptions.AvailableSlots;
                case SortField.CreationTime:
                    return QueryOrder.FieldOptions.Created;
                case SortField.LastUpdated:
                    return QueryOrder.FieldOptions.LastUpdated;
                case SortField.Id:
                    return QueryOrder.FieldOptions.ID;
                case SortField.StringIndex1:
                    return QueryOrder.FieldOptions.S1;
                case SortField.StringIndex2:
                    return QueryOrder.FieldOptions.S2;
                case SortField.StringIndex3:
                    return QueryOrder.FieldOptions.S3;
                case SortField.StringIndex4:
                    return QueryOrder.FieldOptions.S4;
                case SortField.StringIndex5:
                    return QueryOrder.FieldOptions.S5;
                case SortField.NumberIndex1:
                    return QueryOrder.FieldOptions.N1;
                case SortField.NumberIndex2:
                    return QueryOrder.FieldOptions.N2;
                case SortField.NumberIndex3:
                    return QueryOrder.FieldOptions.N3;
                case SortField.NumberIndex4:
                    return QueryOrder.FieldOptions.N4;
                case SortField.NumberIndex5:
                    return QueryOrder.FieldOptions.N5;
            }

            throw new Exception("Invalid SortField");
        }

        internal static SessionException ToSessionException(LobbyServiceException exception)
        {
            switch (exception.Reason)
            {
                case LobbyExceptionReason.LobbyNotFound:
                    return new SessionException(exception.Message, SessionError.SessionNotFound);
                case LobbyExceptionReason.LobbyConflict:
                    return new SessionException(exception.Message, SessionError.SessionConflict);
                case LobbyExceptionReason.RateLimited:
                    return new SessionException(exception.Message, SessionError.RateLimitExceeded);
                case LobbyExceptionReason.MigrationDataRequestTimeout:
                    return new SessionException(exception.Message, SessionError.MigrationDataRequestTimeout);
                case LobbyExceptionReason.Forbidden when exception.Message is k_MultiplayServerAttemptingToConnectAnExistingSessionErrorMessage:
                    return new SessionException(
                        "A Session with the same identifier already exists, you are likely seeing this error message due to a GUID collision."
                        , SessionError.InvalidSessionIdentifier);
                case LobbyExceptionReason.LobbyVersionDoesNotMatch:
                    return new SessionException("Unable to update session because the internal local state is outdated. Please retry the operation.", SessionError.OutOfSyncSession);
                default:
                    return new SessionException(exception.Message, SessionError.Unknown);
            }
        }
    }
}
