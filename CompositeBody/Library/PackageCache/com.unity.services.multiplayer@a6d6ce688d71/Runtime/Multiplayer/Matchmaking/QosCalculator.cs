using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.Services.Matchmaker.Models;
using Unity.Services.Qos;
using Unity.Services.Qos.V2.Models;
using UnityEngine;

namespace Unity.Services.Multiplayer
{
    internal class QosCalculator
    {
        readonly IQosService m_QosService;

        const string k_UnknownRegion = "unknown-region";


        public QosCalculator(IQosService qosService)
        {
            m_QosService = qosService;
        }

        public async Task<List<QosResult>> GetQosResultsAsync(string queueName)
        {
            var allQosServers = await m_QosService.GetAllServersAsync();

            if (allQosServers == null)
                throw new SessionException("Could not find QoS servers for queue", SessionError.QoSMeasurementFailed);

            var qosServersForQueue = allQosServers
                .Where(q => q.Annotations.MatchmakerQueueName != null)
                .Where(q => q.Annotations.MatchmakerQueueName.Contains(queueName))
                .Where(q => ExtractRegion(q.Annotations) != k_UnknownRegion)
                .ToList();

            if (qosServersForQueue.Count == 0)
                return new List<QosResult>();

            var measurements = await m_QosService.GetQosResultsAsync(qosServersForQueue);
            if (measurements == null)
                throw new SessionException("Could not measure QoS", SessionError.QoSMeasurementFailed);

            try
            {
                return measurements
                    .Where(q =>
                    q.Item2.AverageLatencyMs != int.MaxValue &&
                    q.Item2.AverageLatencyMs >= 0 &&
                    q.Item2.PacketLossPercent >= 0.0 &&
                    q.Item2.PacketLossPercent < 1.0)
                    .GroupBy(q => ExtractRegion(q.Item1.Annotations))
                    .Select(q => new QosResult(
                        regionId: q.Key,
                        packetLoss: q.Average(t => t.Item2.PacketLossPercent),
                        latency: q.Average(t => t.Item2.AverageLatencyMs),
                        annotations: ExtractPoolId(q.First().Item1.Annotations))).ToList()
                    .OrderBy(q => q.Latency)
                    .ThenBy(q => q.PacketLoss)
                    .ToList();
            }
            catch (Exception e)
            {
                throw new SessionException(e.Message, SessionError.QoSMeasurementFailed);
            }
        }

        static string ExtractRegion(QosServerAnnotations annotations)
        {
            if (annotations.RelayRegionId != null && annotations.RelayRegionId.Count > 0)
                return annotations.RelayRegionId[0];

            if (annotations.MultiplayRegionId != null && annotations.MultiplayRegionId.Count > 0)
                return annotations.MultiplayRegionId[0];

            return k_UnknownRegion;
        }

        static Dictionary<string, List<string>> ExtractPoolId(QosServerAnnotations annotations)
        {
            const string PoolIdKeyName = "MatchmakerPoolId"; // this needs to match what the MM backend expects. See https://gitlab.cds.internal.unity3d.com/ucg/mm-contracts/-/blob/bab71ec1e2fa246681349caa4a3e6361b977b9e8/Unity.Matchmaker.Services.Contracts/QosResult.cs#L9

            if (annotations.MatchmakerPoolId != null && annotations.MatchmakerPoolId.Count > 0)
                return new Dictionary<string, List<string>> { { PoolIdKeyName, annotations.MatchmakerPoolId } };
            return null;
        }
    }
}
