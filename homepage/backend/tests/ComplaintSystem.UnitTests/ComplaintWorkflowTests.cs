using ComplaintSystem.Application.Complaints;
using ComplaintSystem.Domain.Enums;

namespace ComplaintSystem.UnitTests;

public sealed class ComplaintWorkflowTests
{
    [Theory]
    [InlineData(ComplaintStatus.Submitted, ComplaintStatus.Assigned)]
    [InlineData(ComplaintStatus.UnderReview, ComplaintStatus.Resolved)]
    [InlineData(ComplaintStatus.Resolved, ComplaintStatus.Reopened)]
    public void Allows_valid_transitions(ComplaintStatus from, ComplaintStatus to) =>
        Assert.True(ComplaintWorkflow.CanTransition(from, to));

    [Theory]
    [InlineData(ComplaintStatus.Submitted, ComplaintStatus.Closed)]
    [InlineData(ComplaintStatus.Closed, ComplaintStatus.UnderReview)]
    [InlineData(ComplaintStatus.Resolved, ComplaintStatus.Submitted)]
    public void Rejects_invalid_transitions(ComplaintStatus from, ComplaintStatus to) =>
        Assert.False(ComplaintWorkflow.CanTransition(from, to));

    [Fact]
    public void Critical_deadlines_are_shorter_than_normal_deadlines()
    {
        var critical = ComplaintWorkflow.Deadlines(PriorityLevel.Critical);
        var normal = ComplaintWorkflow.Deadlines(PriorityLevel.Normal);
        Assert.True(critical.FirstResponse < normal.FirstResponse);
        Assert.True(critical.Resolution < normal.Resolution);
    }
}
