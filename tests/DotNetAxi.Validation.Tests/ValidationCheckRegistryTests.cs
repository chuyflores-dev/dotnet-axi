using DotNetAxi.Contracts;

namespace DotNetAxi.Validation.Tests;

public sealed class ValidationCheckRegistryTests
{
    [Fact]
    public void Registered_checks_retain_queryable_effect_policies()
    {
        var registry = new ValidationCheckRegistry();
        var policy = new OperationPolicy(
            OperationClassification.Executing,
            mayAccessNetwork: false,
            mayExecuteRepositoryCode: true,
            mayWriteArtifacts: true,
            mayWriteMetadata: false,
            mayWriteUserState: false,
            mayWriteSource: false);

        registry.Add(new ValidationCheck("build", policy));

        var check = Assert.Single(registry.Checks);
        Assert.Same(check, registry.Get("build"));
        Assert.Same(policy, check.Policy);
    }

    [Fact]
    public void Unclassified_check_is_rejected()
    {
        var exception = Assert.Throws<ArgumentNullException>(
            () => new ValidationCheck("compiler", policy: null!));

        Assert.Equal("policy", exception.ParamName);
    }

    [Fact]
    public void Source_writing_check_cannot_enter_the_registry()
    {
        var registry = new ValidationCheckRegistry();
        var policy = new OperationPolicy(
            OperationClassification.Executing,
            mayAccessNetwork: false,
            mayExecuteRepositoryCode: true,
            mayWriteArtifacts: true,
            mayWriteMetadata: false,
            mayWriteUserState: false,
            mayWriteSource: true);

        var exception = Assert.Throws<ArgumentException>(
            () => registry.Add(new ValidationCheck("format-apply", policy)));

        Assert.Equal("check", exception.ParamName);
        Assert.Empty(registry.Checks);
    }
}
