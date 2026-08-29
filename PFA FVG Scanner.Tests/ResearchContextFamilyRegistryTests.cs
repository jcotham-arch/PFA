using PFA_FVG_Scanner.Domain.Context;

namespace PFA_FVG_Scanner.Tests;

public sealed class ResearchContextFamilyRegistryTests
{
    [Fact]
    public void CatalogRegistersEveryApprovedContextFamilyAndPreservesMissingDataSafety()
    {
        var catalog=new ResearchContextFamilyRegistry().GetCatalog();
        Assert.Equal(20,catalog.Families);
        Assert.Equal(20,catalog.Items.Select(x=>x.FamilyId).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(catalog.Items,x=>x.FamilyId=="order-flow"&&x.AgentFeatureEligible);
        Assert.Contains(catalog.Items,x=>x.FamilyId=="level-two"&&x.Maturity==ResearchContextMaturity.ExternalDataRequired);
        Assert.Contains(catalog.Items,x=>x.FamilyId=="seasonality");
        Assert.Contains(catalog.Items,x=>x.FamilyId=="position-sizing");
        Assert.All(catalog.Items,x=>Assert.True(x.MissingDataMustRemainNull));
        Assert.All(catalog.Items.Where(x=>x.Maturity==ResearchContextMaturity.ExternalDataRequired),x=>Assert.False(x.AgentFeatureEligible));
    }
}
