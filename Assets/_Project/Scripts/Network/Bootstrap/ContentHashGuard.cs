using System;
using System.Collections.Generic;
using System.Text;
using Robogame.Block;
using Robogame.Combat;
using Robogame.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Robogame.Network.Bootstrap
{
    /// <summary>
    /// Bucket-A content guard (NETCODE_PLAN §6 / §13). Every machine loads
    /// its block / impact config independently; if a client's loaded content
    /// differs from the server's, "block 47" means different things on each
    /// side. We never replicate this config — instead we hash it and reject
    /// a mismatched client at connection time with a clear reason.
    /// </summary>
    /// <remarks>
    /// Implemented through NGO connection approval rather than a post-spawn
    /// RPC so a bad client is turned away <em>before</em> it spawns. The
    /// client puts its 4-byte content hash in
    /// <c>NetworkConfig.ConnectionData</c>; the server's
    /// <see cref="ApproveConnection"/> compares it to its own. Reuses
    /// <see cref="BlueprintBlob.Crc32"/> — same cheap mismatch detector,
    /// not a security hash. Phase 1 loopback always matches (same build);
    /// the guard earns its keep once builds can diverge.
    /// </remarks>
    public static class ContentHashGuard
    {
        private const char Sep = '\n';

        /// <summary>
        /// Deterministic fingerprint of the Bucket-A content set: every
        /// <see cref="BlockDefinition.Id"/> (sorted, ordinal) plus the
        /// <see cref="ImpactConfig"/> ramming constants. Sorting makes it
        /// order-independent; floats go through the bit pattern so the
        /// hash is exact, not culture-formatted.
        /// </summary>
        public static uint ComputeLocalContentHash()
        {
            var sb = new StringBuilder(512);

            BlockDefinitionLibrary lib = GameStateController.Instance != null
                ? GameStateController.Instance.Library
                : null;
            if (lib != null)
            {
                var ids = new List<string>(lib.Definitions.Count);
                foreach (BlockDefinition def in lib.Definitions)
                {
                    if (def != null && !string.IsNullOrEmpty(def.Id)) ids.Add(def.Id);
                }
                ids.Sort(StringComparer.Ordinal);
                for (int i = 0; i < ids.Count; i++) sb.Append(ids[i]).Append(Sep);
            }
            else
            {
                // No library resolvable yet — still deterministic per build,
                // so a loopback pair (identical build) matches. Flagged as a
                // Phase-1 limitation in the session log.
                sb.Append("<no-library>");
            }

            ImpactConfig ic = ImpactConfig.Instance;
            AppendFloat(sb, ic.DamagePerKj);
            AppendFloat(sb, ic.MinSpeed);
            AppendFloat(sb, ic.Ring0Scale);
            AppendFloat(sb, ic.Ring1Scale);
            AppendFloat(sb, ic.Ring2Scale);

            return BlueprintBlob.Crc32(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static void AppendFloat(StringBuilder sb, float v)
            => sb.Append(BitConverter.SingleToInt32Bits(v)).Append(Sep);

        /// <summary>Stamp the local content hash into the connection
        /// payload the client/host sends on connect.</summary>
        public static void PrepareLocalConnectionData(NetworkManager nm)
        {
            if (nm == null) return;
            nm.NetworkConfig.ConnectionData = BitConverter.GetBytes(ComputeLocalContentHash());
        }

        /// <summary>
        /// Server-side connection-approval callback. Approves a client only
        /// when its content hash matches the server's. No player object is
        /// created here — Phase 1 spawns robots explicitly in Step 4.
        /// </summary>
        public static void ApproveConnection(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            uint serverHash = ComputeLocalContentHash();
            uint clientHash = request.Payload != null && request.Payload.Length >= 4
                ? BitConverter.ToUInt32(request.Payload, 0)
                : 0u;

            if (clientHash != serverHash)
            {
                response.Approved = false;
                response.CreatePlayerObject = false;
                response.Reason =
                    $"Content mismatch (server 0x{serverHash:X8}, client 0x{clientHash:X8}). " +
                    "Both machines must run the same build / block content.";
                Debug.LogWarning($"[ContentHashGuard] Rejected client {request.ClientNetworkId}: {response.Reason}");
                return;
            }

            response.Approved = true;
            response.CreatePlayerObject = false; // robots spawned explicitly (Step 4)
        }
    }
}
