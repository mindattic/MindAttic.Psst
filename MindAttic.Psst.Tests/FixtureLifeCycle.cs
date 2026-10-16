using NUnit.Framework;

// Each test gets a fresh instance of its class, as it did under xUnit, so per-test state set up in
// constructors and field initialisers never leaks from one test into the next.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
