using System.Text.Json;
using TrackLab.Api.Audit;

namespace TrackLab.Api.Tests.Unit;

public class AuditDiffTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Create_lists_every_new_field_except_bookkeeping_columns()
    {
        var changes = AuditDiff.Compute(null, Json("""{"code":"QA","description":"QA Check","mandays":null,"id":"1","createdBy":"me"}"""));

        Assert.Equal(new[] { "code", "description" }, changes.Select(c => c.Field));
        Assert.All(changes, c => Assert.Null(c.OldValue));
    }

    [Fact]
    public void Update_lists_only_fields_that_really_changed()
    {
        var before = Json("""{"data":{"tracker_code":"A","description":"Old","order":2,"total_fee":400000.00,"received_date":"2025-06-23T00:00:00"}}""");
        var after = Json("""{"trackerCode":"A","description":"New","order":2,"totalFee":400000,"receivedDate":"2025-06-23T00:00:00Z","updatedBy":"me"}""");

        var change = Assert.Single(AuditDiff.Compute(before, after));

        Assert.Equal("description", change.Field);
        Assert.Equal("Old", change.OldValue);
        Assert.Equal("New", change.NewValue);
    }

    [Fact]
    public void Unwrap_returns_the_record_inside_a_data_envelope()
    {
        var unwrapped = AuditDiff.Unwrap(Json("""{"data":{"id":"1"},"success":true}"""));

        Assert.Equal("1", unwrapped!.Value.GetProperty("id").GetString());
    }
}
