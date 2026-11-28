using System.Data;
using System.Data.Common;
using DubUrl.Adomd.Mapping;
using DubUrl.Adomd.Wrappers;
using DubUrl.Discovery;
using DubUrl.Mapping;
using DubUrl.Parsing;
using Moq;
using Moq.Protected;
using NUnit.Framework;

namespace DubUrl.Schema.Testing;

public class AnalyticalMetadataInspectorTests
{
    private const string SourceUrl = "ssasdax://localhost/Sales";

    [Test]
    public async Task BorrowedInspectionUsesTheRegisteredReaderAndLeavesTheConnectionOpen()
    {
        var connection = new Mock<DbConnection>();
        connection.SetupGet(value => value.State).Returns(ConnectionState.Open);
        var expected = new AnalyticalMetadata([], [], new Dictionary<AnalyticalFeature, AnalyticalCoverage>(), []);
        var reader = new Mock<IAnalyticalMetadataReader>();
        reader.Setup(value => value.ReadAsync(connection.Object, default)).ReturnsAsync(expected);
        var source = new ConnectionUrl(SourceUrl, Registry(reader.Object));

        var result = await new AnalyticalMetadataInspector().InspectAsync(source, connection.Object);

        Assert.That(result, Is.SameAs(expected));
        connection.Verify(value => value.Close(), Times.Never());
        connection.Protected().Verify("Dispose", Times.Never(), new object[] { true });
    }

    [Test]
    public async Task UrlInspectionOwnsAndDisposesTheOpenedConnection()
    {
        var connection = new Mock<DbConnection>();
        var expected = new AnalyticalMetadata([], [], new Dictionary<AnalyticalFeature, AnalyticalCoverage>(), []);
        var reader = new Mock<IAnalyticalMetadataReader>();
        reader.Setup(value => value.ReadAsync(connection.Object, default)).ReturnsAsync(expected);
        var source = new Mock<ConnectionUrl>(SourceUrl, Registry(reader.Object));
        source.Setup(value => value.OpenAsync(default(CancellationToken)))
            .ReturnsAsync(connection.Object);

        var result = await new AnalyticalMetadataInspector().InspectAsync(source.Object);

        Assert.That(result, Is.SameAs(expected));
        source.Verify(value => value.OpenAsync(default(CancellationToken)), Times.Once);
        connection.Protected().Verify("Dispose", Times.Once(), new object[] { true });
    }

    [Test]
    public async Task MissingCapabilityReturnsUnsupportedWithoutOpeningTheConnection()
    {
        var source = new Mock<ConnectionUrl>(SourceUrl, Mock.Of<ISchemeRegistry>());

        var result = await new AnalyticalMetadataInspector().InspectAsync(source.Object);

        Assert.That(result.Coverage.Values, Is.All.EqualTo(AnalyticalCoverage.Unsupported));
        source.Verify(value => value.OpenAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ConnectionFailureReturnsAProviderNeutralDiagnostic()
    {
        var failure = new ApplicationException("Driver details");
        var reader = new Mock<IAnalyticalMetadataReader>();
        reader.Setup(value => value.IsAccessFailure(failure)).Returns(true);
        var source = new Mock<ConnectionUrl>(SourceUrl, Registry(reader.Object));
        source.Setup(value => value.OpenAsync(default(CancellationToken))).ThrowsAsync(failure);

        var result = await new AnalyticalMetadataInspector().InspectAsync(source.Object);

        Assert.Multiple(() =>
        {
            Assert.That(result.Coverage.Values, Is.All.EqualTo(AnalyticalCoverage.Inaccessible));
            Assert.That(result.Diagnostics.Single().Message, Does.Not.Contain("Driver details"));
        });
    }

    [Test]
    public void PreCancelledInspectionDoesNotOpenAnEndpoint()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new Mock<ConnectionUrl>(SourceUrl, Mock.Of<ISchemeRegistry>());

        Assert.That(async () => await new AnalyticalMetadataInspector().InspectAsync(source.Object, cancellation.Token),
            Throws.TypeOf<OperationCanceledException>());
        source.Verify(value => value.OpenAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCase("DUBURL_ADOMD_TABULAR_TEST_URL", AnalyticalObjectKind.Table, AnalyticalObjectKind.Column)]
    [TestCase("DUBURL_ADOMD_MULTIDIM_TEST_URL", AnalyticalObjectKind.Hierarchy, AnalyticalObjectKind.Level)]
    public async Task LiveInspectionFindsParentChildObjectsAndMeasures(string variable,
        AnalyticalObjectKind parentKind, AnalyticalObjectKind childKind)
    {
        var url = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(url))
            Assert.Ignore($"Set {variable} to an Analysis Services URL for a model with these discovery targets.");

        DbProviderFactories.RegisterFactory("Microsoft.AnalysisServices.AdomdClient", AdomdFactory.Instance);
        var registry = new ProviderCatalogBuilder()
            .AddModule(new AttributeProviderModule(typeof(SsasTabularMapper).Assembly))
            .Build();
        var source = new ConnectionUrl(url!, registry);
        Assert.That(source.TryGetCapability<AnalyticalDiscoveryCapability>(out _), Is.True);
        using var borrowed = await source.OpenAsync();

        var result = await new AnalyticalMetadataInspector().InspectAsync(source, borrowed);
        var parentIds = result.Objects.Where(item => item.Id.Kind == parentKind).Select(item => item.Id).ToHashSet();
        var matchingChildren = result.Objects.Where(item => item.Id.Kind == childKind
            && item.Parent is not null && parentIds.Contains(item.Parent));

        Assert.Multiple(() =>
        {
            Assert.That(matchingChildren, Is.Not.Empty);
            Assert.That(result.Objects.Any(item => item.Id.Kind == AnalyticalObjectKind.Measure), Is.True);
            Assert.That(borrowed.State, Is.EqualTo(ConnectionState.Open));
        });
    }

    private static ISchemeRegistry Registry(IAnalyticalMetadataReader reader)
    {
        var capability = new AnalyticalDiscoveryCapability(reader);
        var registry = new Mock<ISchemeRegistry>();
        registry.Setup(value => value.TryGetCapability(It.IsAny<SchemeKey>(), out capability!)).Returns(true);
        return registry.Object;
    }
}
