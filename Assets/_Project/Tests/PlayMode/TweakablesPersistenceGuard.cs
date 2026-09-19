using NUnit.Framework;
using Robogame.Core;

/// <summary>
/// F-063: namespace-less on purpose, so NUnit applies it to every test in
/// this assembly. The rig, the live Editor and the player's own game share
/// one persistentDataPath; without this, any test that calls
/// Tweakables.Set rewrites the player's real tweakables.json.
/// </summary>
[SetUpFixture]
public sealed class TweakablesPersistenceGuard
{
    [OneTimeSetUp]
    public void SuspendPersistence() => Tweakables.PersistenceSuspended = true;

    [OneTimeTearDown]
    public void ResumePersistence() => Tweakables.PersistenceSuspended = false;
}
