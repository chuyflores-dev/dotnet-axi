using DotNetAxi.Contracts;

namespace DotNetAxi.Contracts.Tests;

public sealed class OperationPolicyTests
{
    [Fact]
    public void Every_effect_category_is_explicit_and_queryable()
    {
        var policy = new OperationPolicy(
            OperationClassification.Executing,
            mayAccessNetwork: true,
            mayExecuteRepositoryCode: true,
            mayWriteArtifacts: true,
            mayWriteMetadata: true,
            mayWriteUserState: true,
            mayWriteSource: true);

        Assert.Equal(
            OperationClassification.Executing,
            policy.Classification);
        Assert.True(policy.MayAccessNetwork);
        Assert.True(policy.MayExecuteRepositoryCode);
        Assert.True(policy.MayWriteArtifacts);
        Assert.True(policy.MayWriteMetadata);
        Assert.True(policy.MayWriteUserState);
        Assert.True(policy.MayWriteSource);
    }

    [Theory]
    [InlineData(true, false, false, "mayAccessNetwork")]
    [InlineData(false, true, false, "mayExecuteRepositoryCode")]
    [InlineData(false, false, true, "mayWriteSource")]
    public void Passive_policy_rejects_contradictory_effects(
        bool mayAccessNetwork,
        bool mayExecuteRepositoryCode,
        bool mayWriteSource,
        string parameterName)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new OperationPolicy(
                OperationClassification.Passive,
                mayAccessNetwork,
                mayExecuteRepositoryCode,
                mayWriteArtifacts: false,
                mayWriteMetadata: false,
                mayWriteUserState: false,
                mayWriteSource));

        Assert.Equal(parameterName, exception.ParamName);
    }

    [Fact]
    public void Executing_policy_requires_repository_code_effect()
    {
        var exception = Assert.Throws<ArgumentException>(
            () => new OperationPolicy(
                OperationClassification.Executing,
                mayAccessNetwork: false,
                mayExecuteRepositoryCode: false,
                mayWriteArtifacts: false,
                mayWriteMetadata: false,
                mayWriteUserState: false,
                mayWriteSource: false));

        Assert.Equal("mayExecuteRepositoryCode", exception.ParamName);
    }
}
