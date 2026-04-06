namespace CrudKit.Api.Tenancy;

/// <summary>
/// Defines which roles are allowed cross-tenant access and at what level.
/// </summary>
public class CrossTenantPolicy
{
    internal List<CrossTenantRule> Rules { get; } = new();

    /// <summary>Allow full cross-tenant access for these roles.</summary>
    public CrossTenantPolicy Allow(params string[] roles)
    {
        foreach (var role in roles)
            Rules.Add(new CrossTenantRule(role, CrossTenantAccessLevel.Full));
        return this;
    }

    /// <summary>Allow read-only cross-tenant access for these roles.</summary>
    public CrossTenantRuleBuilder AllowReadOnly(params string[] roles)
    {
        var rules = new List<CrossTenantRule>();
        foreach (var role in roles)
        {
            var rule = new CrossTenantRule(role, CrossTenantAccessLevel.ReadOnly);
            Rules.Add(rule);
            rules.Add(rule);
        }
        return new CrossTenantRuleBuilder(this, rules);
    }
}

/// <summary>
/// Fluent builder for restricting cross-tenant rules to specific entity types.
/// </summary>
public class CrossTenantRuleBuilder
{
    private readonly CrossTenantPolicy _policy;
    private readonly List<CrossTenantRule> _rules;

    internal CrossTenantRuleBuilder(CrossTenantPolicy policy, List<CrossTenantRule> rules)
    {
        _policy = policy;
        _rules = rules;
    }

    /// <summary>Restrict cross-tenant access to only this entity type.</summary>
    public CrossTenantPolicy Only<T1>() where T1 : class
    {
        foreach (var r in _rules) r.AllowedEntityTypes = new HashSet<Type> { typeof(T1) };
        return _policy;
    }

    /// <summary>Restrict cross-tenant access to only these entity types.</summary>
    public CrossTenantPolicy Only<T1, T2>() where T1 : class where T2 : class
    {
        foreach (var r in _rules) r.AllowedEntityTypes = new HashSet<Type> { typeof(T1), typeof(T2) };
        return _policy;
    }

    /// <summary>Restrict cross-tenant access to only these entity types.</summary>
    public CrossTenantPolicy Only<T1, T2, T3>() where T1 : class where T2 : class where T3 : class
    {
        foreach (var r in _rules) r.AllowedEntityTypes = new HashSet<Type> { typeof(T1), typeof(T2), typeof(T3) };
        return _policy;
    }
}

/// <summary>
/// A single cross-tenant access rule for a specific role.
/// </summary>
public class CrossTenantRule
{
    public string Role { get; }
    public CrossTenantAccessLevel AccessLevel { get; }

    /// <summary>
    /// When null, the rule applies to all entity types.
    /// When set, the rule only applies to the specified entity types.
    /// </summary>
    public HashSet<Type>? AllowedEntityTypes { get; set; }

    public CrossTenantRule(string role, CrossTenantAccessLevel accessLevel)
    {
        Role = role;
        AccessLevel = accessLevel;
    }
}

/// <summary>
/// Defines the level of cross-tenant access granted by a rule.
/// </summary>
public enum CrossTenantAccessLevel
{
    /// <summary>Read + write + delete access across tenants.</summary>
    Full,

    /// <summary>Only List + Get operations across tenants.</summary>
    ReadOnly
}
