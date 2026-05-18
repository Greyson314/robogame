using NUnit.Framework;
using Robogame.Core;

namespace Robogame.Tests.EditMode.Gameplay
{
    /// <summary>
    /// Pins the load-bearing invariant of the whole netcode pass: when no
    /// NGO session is registered, <see cref="NetworkContext"/> answers
    /// "I am the authoritative instance" everywhere, so every Step-9
    /// server-gated action still runs and singleplayer is byte-identical.
    /// A regression here silently disables respawn / scoring / bot spawn
    /// in singleplayer — exactly the failure this pass must never cause.
    /// </summary>
    public sealed class NetworkContextTests
    {
        private sealed class FakeContext : INetworkContext
        {
            public bool IsServer { get; set; }
            public bool IsClient { get; set; }
            public bool IsHost { get; set; }
            public bool IsOnline { get; set; }
        }

        [TearDown]
        public void Reset()
        {
            // Leave the static registry clean for other tests.
            NetworkContext.Unregister(NetworkContext.Instance);
        }

        [Test]
        public void OfflineDefault_IsAuthoritativeEverywhere()
        {
            Assert.IsFalse(NetworkContext.HasActiveContext,
                "No session registered → no active context.");
            INetworkContext ctx = NetworkContext.Instance;
            Assert.IsTrue(ctx.IsServer, "Offline must be authoritative (singleplayer byte-identical).");
            Assert.IsTrue(ctx.IsClient, "Offline must also be a client (local player view).");
            Assert.IsFalse(ctx.IsHost, "Offline is not a networked host.");
            Assert.IsFalse(ctx.IsOnline, "Offline is not an online session.");
        }

        [Test]
        public void Register_ThenUnregister_RevertsToOfflineAuthoritative()
        {
            var fake = new FakeContext { IsServer = false, IsClient = true, IsHost = false, IsOnline = true };
            NetworkContext.Register(fake);

            Assert.IsTrue(NetworkContext.HasActiveContext);
            Assert.AreSame(fake, NetworkContext.Instance);
            Assert.IsFalse(NetworkContext.Instance.IsServer,
                "A registered client context must report non-authoritative.");
            Assert.IsTrue(NetworkContext.Instance.IsOnline);

            NetworkContext.Unregister(fake);

            Assert.IsFalse(NetworkContext.HasActiveContext);
            Assert.IsTrue(NetworkContext.Instance.IsServer,
                "After teardown we must fall back to the offline-authoritative stub.");
            Assert.IsFalse(NetworkContext.Instance.IsOnline);
        }

        [Test]
        public void Unregister_OfDifferentContext_IsIgnored()
        {
            var a = new FakeContext { IsServer = false };
            var b = new FakeContext { IsServer = false };
            NetworkContext.Register(a);

            NetworkContext.Unregister(b); // not the active one — must be a no-op

            Assert.IsTrue(NetworkContext.HasActiveContext);
            Assert.AreSame(a, NetworkContext.Instance);
        }

        [Test]
        public void RegisterNull_IsIgnored_StaysOffline()
        {
            NetworkContext.Register(null);
            Assert.IsFalse(NetworkContext.HasActiveContext);
            Assert.IsTrue(NetworkContext.Instance.IsServer);
        }
    }
}
