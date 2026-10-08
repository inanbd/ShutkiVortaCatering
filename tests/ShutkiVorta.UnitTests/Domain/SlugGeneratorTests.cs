using ShutkiVorta.Domain.Common;

namespace ShutkiVorta.UnitTests.Domain;

public sealed class SlugGeneratorTests
{
    [Theory]
    [InlineData("Loitta Shutki Vorta", "loitta-shutki-vorta")]
    [InlineData("  Aloo   Vorta!! ", "aloo-vorta")]
    [InlineData("Shutki & Rice", "shutki-and-rice")]
    [InlineData("Crème Brûlée", "creme-brulee")]
    [InlineData("--already-a-slug--", "already-a-slug")]
    [InlineData("শুঁটকি ভর্তা", "")]
    [InlineData(null, "")]
    public void Generate_ProducesUrlFriendlySlugs(string? input, string expected) =>
        Assert.Equal(expected, SlugGenerator.Generate(input));

    [Fact]
    public void Generate_TruncatesLongText()
    {
        var slug = SlugGenerator.Generate(new string('a', 300));
        Assert.Equal(SlugGenerator.MaxLength, slug.Length);
    }

    [Theory]
    [InlineData("aloo-vorta", true)]
    [InlineData("Aloo-Vorta", false)]
    [InlineData("aloo--vorta", false)]
    [InlineData("", false)]
    public void IsValid_RecognisesCanonicalSlugs(string slug, bool expected) =>
        Assert.Equal(expected, SlugGenerator.IsValid(slug));
}
