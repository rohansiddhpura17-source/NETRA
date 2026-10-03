using FluentAssertions;
using NETRA.Application.Risk;
using NETRA.Infrastructure.Services;
using Xunit;

namespace NETRA.Tests.Application;

/// <summary>
/// Rule-level tests for <see cref="AnomalyDetector"/>: every rule is comparative
/// (window versus its baseline), every result carries an explanation, and a merely
/// large but stable number never raises an anomaly.
/// </summary>
public sealed class AnomalyDetectorTests
{
    private static readonly Guid DistrictId = Guid.NewGuid();
    private readonly AnomalyDetector _detector = new();

    private static AnomalyDetectionInput Input(
        int current,
        int baseline,
        int currentHigh = 0,
        int baselineHigh = 0,
        int leadingCount = 0,
        string leadingType = "Theft",
        IReadOnlyDictionary<string, IReadOnlyList<int>>? weeklyByType = null) => new()
        {
            DistrictId = DistrictId,
            DistrictName = "Central",
            CurrentCount = current,
            BaselineCount = baseline,
            CurrentHighSeverityCount = currentHigh,
            BaselineHighSeverityCount = baselineHigh,
            LeadingCrimeTypeCount = leadingCount,
            LeadingCrimeType = leadingType,
            WeeklyCountsByType = weeklyByType
                ?? new Dictionary<string, IReadOnlyList<int>> { [leadingType] = [0, 0, 0, 0] }
        };

    // ------------------------------------------------------------ rule 1: volume spike

    [Fact]
    public void Detect_StableHighVolume_ProducesNoAnomaly()
    {
        // 20 cases is a lot of activity, but versus a baseline of 18 it is not unusual.
        var results = _detector.Detect(Input(
            current: 20, baseline: 18,
            currentHigh: 3, baselineHigh: 4,
            leadingCount: 10,
            weeklyByType: new Dictionary<string, IReadOnlyList<int>> { ["Theft"] = [5, 5, 0, 0] }));

        results.Should().BeEmpty(
            "a high value alone must never create an anomaly; detection is comparative");
    }

    [Fact]
    public void Detect_MildIncrease_DoesNotSpike()
    {
        // 5 versus 4: increase, but below both the +50% ratio and sample guard.
        var results = _detector.Detect(Input(current: 5, baseline: 4));

        results.Should().NotContain(a => a.AnomalyType == AnomalyTypes.VolumeSpike);
    }

    [Fact]
    public void Detect_VolumeSpike_ExplainsDeviationAndIsCriticalWhenDoubled()
    {
        var results = _detector.Detect(Input(current: 9, baseline: 4));

        var spike = results.Single(a => a.AnomalyType == AnomalyTypes.VolumeSpike);
        spike.Title.Should().Be("Crime volume up 125% in Central");
        spike.Description.Should().Be(
            "Crime volume in Central increased by 125% compared with the previous comparison period (9 cases versus 4).");
        spike.Severity.Should().Be("Critical");
        spike.DeviationPercent.Should().Be(125m);
        // 9 of the combined 13 observations fall inside the window.
        spike.Confidence.Should().Be(0.692m);
    }

    [Fact]
    public void Detect_NewActivityWithoutBaseline_ExplainsAbsenceOfComparison()
    {
        var results = _detector.Detect(Input(current: 5, baseline: 0));

        var spike = results.Single(a => a.AnomalyType == AnomalyTypes.VolumeSpike);
        spike.Title.Should().Be("New activity concentrated in Central");
        spike.Description.Should().Contain("compared with none in the preceding period");
        spike.DeviationPercent.Should().BeNull("no percentage is defined against a zero baseline");
        spike.Severity.Should().Be("High");
        spike.Confidence.Should().Be(0.99m, "the confidence value is capped below certainty");
    }

    // ------------------------------------------------------------ rule 2: severity shift

    [Fact]
    public void Detect_SeverityShift_ExplainsRiseFromBaseline()
    {
        var results = _detector.Detect(Input(current: 6, baseline: 6, currentHigh: 2, baselineHigh: 1));

        var shift = results.Single(a => a.AnomalyType == AnomalyTypes.SeverityShift);
        shift.Title.Should().Be("Severity shift in Central");
        shift.Description.Should().Be(
            "High or critical severity cases in Central rose from 1 in the comparison period to 2 in the analysis window.");
        shift.Severity.Should().Be("Medium");
    }

    [Fact]
    public void Detect_SeverityStable_NoShiftRaised()
    {
        var results = _detector.Detect(Input(current: 8, baseline: 8, currentHigh: 3, baselineHigh: 3));

        results.Should().NotContain(a => a.AnomalyType == AnomalyTypes.SeverityShift);
    }

    // ------------------------------------------------------------ rule 3: concentration

    [Fact]
    public void Detect_DominantCrimeType_ExplainsShare()
    {
        var results = _detector.Detect(Input(current: 6, baseline: 6, leadingCount: 4));

        var concentration = results.Single(a => a.AnomalyType == AnomalyTypes.CrimeConcentration);
        concentration.Title.Should().Be("Unusual concentration of Theft in Central");
        concentration.Description.Should().Contain("66.7%").And.Contain("4 of 6");
        concentration.Severity.Should().Be("Medium");
        concentration.Confidence.Should().Be(0.667m);
    }

    [Fact]
    public void Detect_LargeButDiverseWindow_NoConcentrationRaised()
    {
        var results = _detector.Detect(Input(
            current: 20, baseline: 18, leadingCount: 10,
            weeklyByType: new Dictionary<string, IReadOnlyList<int>> { ["Theft"] = [5, 5, 0, 0] }));

        results.Should().NotContain(a => a.AnomalyType == AnomalyTypes.CrimeConcentration);
    }

    // ------------------------------------------------------------ rule 4: recurring pattern

    [Fact]
    public void Detect_LeadingTypeActiveInEveryWeek_IsRecurring()
    {
        var results = _detector.Detect(Input(
            current: 8, baseline: 8, leadingCount: 3,
            weeklyByType: new Dictionary<string, IReadOnlyList<int>> { ["Theft"] = [1, 1, 1, 0] }));

        var recurrence = results.Single(a => a.AnomalyType == AnomalyTypes.RecurringPattern);
        recurrence.Title.Should().Be("Recurring Theft pattern in Central");
        recurrence.Description.Should().Be(
            "Theft was recorded in 3 of the last 4 weeks in Central, a repeating rather than one-off pattern.");
        recurrence.Severity.Should().Be("Low");
        recurrence.Confidence.Should().Be(0.75m);
    }

    [Fact]
    public void Detect_LeadingTypeWithGapsInWeeks_IsNotRecurring()
    {
        var results = _detector.Detect(Input(
            current: 8, baseline: 8, leadingCount: 4,
            weeklyByType: new Dictionary<string, IReadOnlyList<int>> { ["Theft"] = [2, 2, 0, 0] }));

        results.Should().NotContain(a => a.AnomalyType == AnomalyTypes.RecurringPattern);
    }

    // ------------------------------------------------------------ shared guarantees

    [Fact]
    public void Detect_EveryCandidateCarriesAnExplanationSentence()
    {
        var results = _detector.Detect(Input(
            current: 9, baseline: 2, currentHigh: 4, baselineHigh: 1, leadingCount: 9,
            weeklyByType: new Dictionary<string, IReadOnlyList<int>> { ["Theft"] = [2, 2, 3, 2] }));

        results.Should().NotBeEmpty();
        results.Should().OnlyContain(a =>
            !string.IsNullOrWhiteSpace(a.Description) &&
            a.Description.EndsWith('.') &&
            a.Description.Contains("Central", StringComparison.Ordinal));
        results.Should().OnlyContain(a =>
            a.Confidence > 0m && a.Confidence <= 1m);
    }

    [Fact]
    public void Detect_SameInputProducesSameOutput()
    {
        var input = Input(
            current: 9, baseline: 2, currentHigh: 4, baselineHigh: 1, leadingCount: 9,
            weeklyByType: new Dictionary<string, IReadOnlyList<int>> { ["Theft"] = [3, 3, 3, 0] });

        var first = _detector.Detect(input);
        var second = _detector.Detect(input);

        second.Select(a => (a.Title, a.Description, a.Severity, a.AnomalyType, a.Confidence))
            .Should().Equal(first.Select(a => (a.Title, a.Description, a.Severity, a.AnomalyType, a.Confidence)));
    }

    [Fact]
    public void AnomalyTypes_LabelsCoverAllDetectionKinds()
    {
        AnomalyTypes.Label(AnomalyTypes.VolumeSpike).Should().Be("Volume spike");
        AnomalyTypes.Label(AnomalyTypes.SeverityShift).Should().Be("Severity shift");
        AnomalyTypes.Label(AnomalyTypes.CrimeConcentration).Should().Be("Crime concentration");
        AnomalyTypes.Label(AnomalyTypes.RecurringPattern).Should().Be("Recurring pattern");
        AnomalyTypes.Label(99).Should().Be("Unusual activity");
    }
}
