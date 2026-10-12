using Tahona.Application.Features.Recovery;
using Tahona.Domain.Entities;
using Tahona.Domain.Enums;

namespace Tahona.Tests;

public class RecoveryScorerTests
{
    private static readonly RecoverySettings Settings = new();

    [Fact]
    public void Spec_example_high_value_recent_patient_is_high_priority()
    {
        // Ahmad Khalil: 450 JOD unstarted plan, 32 days inactive, 3 completed visits, no no-shows.
        var score = RecoveryScorer.Score(new ScoreInput(450m, 29, new PatientSignals(3, 0, 32, false),
            [RecoveryIssueType.TreatmentNotStarted]), Settings);

        Assert.Equal(PriorityLevel.High, score.Priority);
        Assert.InRange(score.Score, 85, 95);
        Assert.Equal("High", score.LikelihoodLabel);
    }

    [Fact]
    public void Small_recall_for_long_lost_patient_is_low_priority()
    {
        var score = RecoveryScorer.Score(new ScoreInput(35m, 0, new PatientSignals(1, 2, 400, false),
            [RecoveryIssueType.RecallDue]), Settings);

        Assert.Equal(PriorityLevel.Low, score.Priority);
        Assert.True(score.Score < Settings.MediumPriorityThreshold);
    }

    [Fact]
    public void Score_is_monotonic_in_value()
    {
        var signals = new PatientSignals(2, 0, 60, false);
        var low = RecoveryScorer.Score(new ScoreInput(50m, 5, signals, [RecoveryIssueType.TreatmentNotStarted]), Settings).Score;
        var high = RecoveryScorer.Score(new ScoreInput(800m, 5, signals, [RecoveryIssueType.TreatmentNotStarted]), Settings).Score;
        Assert.True(high > low);
    }

    [Fact]
    public void No_shows_reduce_return_likelihood()
    {
        var reliable = RecoveryScorer.ReturnLikelihood(new PatientSignals(4, 0, 30, false));
        var unreliable = RecoveryScorer.ReturnLikelihood(new PatientSignals(4, 3, 30, false));
        Assert.True(reliable > unreliable);
    }

    [Fact]
    public void Multiple_issue_types_raise_severity_but_cap_at_one()
    {
        Assert.Equal(0.8, RecoveryScorer.Severity([RecoveryIssueType.TreatmentNotStarted]), 3);
        Assert.Equal(0.9, RecoveryScorer.Severity([RecoveryIssueType.TreatmentNotStarted, RecoveryIssueType.OutstandingBalance]), 3);
        Assert.Equal(1.0, RecoveryScorer.Severity([RecoveryIssueType.TreatmentUnfinished, RecoveryIssueType.MissedAppointment, RecoveryIssueType.RecallDue]), 3);
    }

    [Fact]
    public void Weights_are_configurable_and_components_sum_to_score()
    {
        var valueOnly = new RecoverySettings { WeightValue = 1, WeightUrgency = 0, WeightLikelihood = 0, WeightSeverity = 0 };
        var score = RecoveryScorer.Score(new ScoreInput(400m, 0, new PatientSignals(0, 0, 900, false), [RecoveryIssueType.RecallDue]), valueOnly);
        Assert.Equal(100, score.Score);
        Assert.Equal(score.Score, (int)Math.Round(score.Components.Sum(c => c.Points)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-10, 0)]
    public void Never_produces_out_of_range_scores(decimal value, int overdue)
    {
        var score = RecoveryScorer.Score(new ScoreInput(value, overdue, new PatientSignals(0, 10, 0, false), []), Settings);
        Assert.InRange(score.Score, 0, 100);
    }
}
