using Planizer.MsSql;

namespace Planizer.Tests;

/// <summary>
/// The explicit rule registry exists so that Native AOT trimming cannot silently drop rules
/// (reflection discovery kept only 1 of 52 rules in the first AOT build). Reflection lives on
/// here, as the completeness check for the hand-maintained list.
/// </summary>
public class RuleRegistryTests
{
    private static IReadOnlyList<Type> ReflectedRuleTypes()
        => typeof(MsSqlAnalyzer).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract
                && t.IsAssignableTo(typeof(MsSqlRuleBase))
                && t.GetConstructor(Type.EmptyTypes) is not null)
            .ToList();

    [Fact]
    public void Registry_contains_every_rule_class_in_the_assembly()
    {
        var reflected = ReflectedRuleTypes().Select(t => t.FullName).Order().ToList();
        var registered = RuleRegistry.CreateAll().Select(r => r.GetType().FullName).Order().ToList();

        Assert.Equal(reflected, registered);
    }

    [Fact]
    public void DiscoverRules_returns_registry_rules_ordered_by_id()
    {
        var rules = MsSqlAnalyzer.DiscoverRules();

        Assert.Equal(ReflectedRuleTypes().Count, rules.Count);
        Assert.Equal(rules.Select(r => r.Id).OrderBy(id => id, StringComparer.Ordinal), rules.Select(r => r.Id));
    }

    [Fact]
    public void Registry_creates_fresh_instances_on_every_call()
    {
        var first = RuleRegistry.CreateAll();
        var second = RuleRegistry.CreateAll();

        Assert.All(first.Zip(second), pair => Assert.NotSame(pair.First, pair.Second));
    }
}
