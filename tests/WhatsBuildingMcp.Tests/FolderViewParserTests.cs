using WhatsBuildingMcp.Services;

namespace WhatsBuildingMcp.Tests;

public sealed class FolderViewParserTests
{
    private static readonly Uri BaseUri = new("https://www.nanaimo.ca/whatsbuilding/");

    private static string Fixture(string name)
        => File.ReadAllText(Path.Combine("Fixtures", name));

    [Fact]
    public void Parses_Cancelled_Permit_With_Attachments_And_Policies()
    {
        var detail = FolderViewParser.Parse(Fixture("folderview_dp001002.html"), "DP001002", BaseUri);

        Assert.True(detail.Found);
        Assert.Equal("DP001002", detail.FileNumber);
        Assert.Equal("1400 Wingrove Street", detail.CivicAddress);
        Assert.Equal("Departure Bay", detail.Neighbourhood);
        Assert.Equal("Development Permit", detail.ApplicationType);
        Assert.Equal(new DateTime(2016, 5, 24), detail.DateSubmitted);
        Assert.Equal("CANCELLED", detail.Status);
        Assert.Equal(new DateTime(2016, 12, 12), detail.CloseDate);
        Assert.StartsWith("The proposed development is a two storey", detail.Description);
        Assert.Equal("https://www.nanaimo.ca/whatsbuilding/Folder/DP001002", detail.PublicUrl);
        Assert.Equal(49.2040495, detail.Latitude);
        Assert.Equal(-123.9708757, detail.Longitude);

        Assert.NotEmpty(detail.Attachments);
        Assert.Contains(detail.Attachments, a => a.Name == "Location Plan"
            && a.Url == "https://www.nanaimo.ca/whatsbuilding/Files/DP001002/DP001002%20Schedule%20A.jpg"
            && a.PostedDate == new DateTime(2016, 6, 1, 11, 2, 31));

        Assert.NotEmpty(detail.Policies);
        Assert.Contains(detail.Policies, p => p.Title.Contains("7266", StringComparison.Ordinal));

        Assert.NotEmpty(detail.ProcessSteps);
        Assert.Contains(detail.ProcessSteps, s => s.Task == "APPLICATION RECEIVED" && s.Status == "COMPLETED");
    }

    [Fact]
    public void Parses_Active_Rezoning_With_Staff_And_Properties()
    {
        var detail = FolderViewParser.Parse(Fixture("folderview_ra000496.html"), "RA000496", BaseUri);

        Assert.True(detail.Found);
        Assert.Equal("ACTIVE", detail.Status);
        Assert.Equal("Rezoning Application", detail.ApplicationType);
        Assert.Contains("Caleb Horn", detail.StaffContact, StringComparison.Ordinal);
        Assert.Contains("caleb.horn@nanaimo.ca", detail.StaffContact, StringComparison.Ordinal);

        var property = Assert.Single(detail.Properties);
        Assert.Equal("3469 UPLANDS DRIVE", property.Address);
        Assert.Equal("R10", property.Zone);
        Assert.Equal("https://www.nanaimo.ca/ByLaws/ViewBylaw/4500.pdf#nameddest=Residential", property.ZoneUrl);
        Assert.Equal("Property Report", property.MoreText);

        Assert.Equal(8, detail.Attachments.Count);
        Assert.Contains(detail.Attachments, a => a.Name == "Staff Report");
    }

    [Fact]
    public void Parses_Minimal_Permit()
    {
        var detail = FolderViewParser.Parse(Fixture("folderview_bp117814.html"), "BP117814", BaseUri);

        Assert.True(detail.Found);
        Assert.Equal("BP117814", detail.FileNumber);
        Assert.Equal("6671 Aulds Road", detail.CivicAddress);
        Assert.Empty(detail.Attachments);
        Assert.Empty(detail.Notices);
        Assert.Null(detail.EducationalNote);
    }

    [Fact]
    public void Parses_Notices_With_Location_Date_Time_And_Links()
    {
        var detail = FolderViewParser.Parse(Fixture("folderview_ra000507.html"), "RA000507", BaseUri);

        var notice = Assert.Single(detail.Notices);
        Assert.Equal("Council Meeting", notice.Title);
        Assert.Equal("80 Commercial Street, Vancouver Island Conference Centre, Shaw Auditorium", notice.Location);
        Assert.Equal("SEPTEMBER 21, 2026", notice.Date);
        Assert.Equal("7:00pm", notice.Time);
        Assert.Equal(new DateTime(2026, 10, 16, 10, 45, 47), notice.PostedDate);
        Assert.Contains(notice.Links, l => l.Text == "Meeting Agendas" && l.Url.Contains("council-meetings", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_File_Returns_NotFound()
    {
        var detail = FolderViewParser.Parse(Fixture("folderview_notfound.html"), "ZZ999999", BaseUri);

        Assert.False(detail.Found);
        Assert.Equal("ZZ999999", detail.FileNumber);
    }
}
