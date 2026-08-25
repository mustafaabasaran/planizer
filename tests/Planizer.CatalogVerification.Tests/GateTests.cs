namespace Planizer.CatalogVerification.Tests;

/// <summary>
/// The "never runs locally" guarantee: [VerifyFact] skips whenever the gate variable is not
/// set, and an uninitialized fixture refuses access instead of connecting anywhere.
/// </summary>
public sealed class GateTests
{
    [Fact]
    public void VerifyFact_skips_exactly_when_the_gate_is_off()
    {
        var attribute = new VerifyFactAttribute();
        if (VerificationGate.IsEnabled)
        {
            Assert.Null(attribute.Skip);
        }
        else
        {
            Assert.NotNull(attribute.Skip);
            Assert.Contains(VerificationGate.GateVariable, attribute.Skip);
        }
    }

    [Fact]
    public void Uninitialized_fixture_refuses_access_instead_of_connecting()
    {
        // Constructing the fixture is safe everywhere: it only becomes live in InitializeAsync,
        // and only behind the gate. Accessing it without initialization must throw.
        var fixture = new ServerFixture();
        Assert.Throws<InvalidOperationException>(() => _ = fixture.ConnectionString);
        Assert.Throws<InvalidOperationException>(() => _ = fixture.Edition);
        Assert.Throws<InvalidOperationException>(() => _ = fixture.Version);
        Assert.Throws<InvalidOperationException>(() => _ = fixture.EditionDescription);
    }
}
