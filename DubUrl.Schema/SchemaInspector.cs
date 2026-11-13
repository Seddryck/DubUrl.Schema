using DubUrl.Discovery;
using DubUrl.Schema.Constraints;
using System.Data.Common;

namespace DubUrl.Schema;

public sealed record SchemaInspectionResult(Schema? Schema, bool IsComplete, bool IsSuccess,
    IReadOnlyDictionary<SchemaFeature, MetadataCoverage> Coverage, IReadOnlyList<DiscoveryDiagnostic> Diagnostics)
{
    public IReadOnlyList<MetadataView> ViewInventory { get; init; } = [];
}

/// <summary>Maps provider-neutral metadata. No SQL is executed by this class.</summary>
public sealed class SchemaInspector
{
    public async Task<SchemaInspectionResult> InspectAsync(ConnectionUrl connection, DiscoveryOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(connection);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (!connection.TryGetCapability<SchemaDiscoveryCapability>(out var capability))
                return Failure("SDI001", MetadataCoverage.Unsupported, "The provider does not support schema discovery.");
            using var owned = await connection.OpenAsync(capability!.Reader.ConfigureOwnedConnection, cancellationToken).ConfigureAwait(false);
            return await InspectAsync(owned, capability!.Reader, options, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException or DubUrlException or NotSupportedException or ArgumentException or FormatException)
        {
            // Driver exception messages can contain credentials and connection strings.
            return Failure("SDI002", MetadataCoverage.Inaccessible, "Database metadata could not be accessed.");
        }
    }

    public async Task<SchemaInspectionResult> InspectAsync(DbConnection borrowed, ISchemaMetadataReader reader,
        DiscoveryOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(borrowed);
        ArgumentNullException.ThrowIfNull(reader);
        options ??= new();
        if (ReferenceEquals(options.Features, new DiscoveryOptions().Features))
        {
            var capabilities = reader.Capabilities;
            var annotations = new[] { SchemaFeature.Annotations, SchemaFeature.ColumnAnnotations }
                .Where(feature => capabilities?.TryGetValue(feature, out var coverage) == true && coverage == MetadataCoverage.Available)
                .ToArray();
            if (annotations.Length > 0)
                options = options with { Features = [.. options.Features, .. annotations] };
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (borrowed.State != System.Data.ConnectionState.Open)
            throw new ArgumentException("A borrowed connection must already be open.", nameof(borrowed));
        SchemaMetadata metadata;
        IReadOnlyDictionary<StructuredColumnIdentity, StructuredColumnMetadata>? structuredColumns = null;
        try
        {
            if (reader is IStructuredColumnMetadataReader structuredReader)
            {
                var result = await structuredReader.ReadWithStructuredTypesAsync(borrowed, options, cancellationToken).ConfigureAwait(false);
                (metadata, structuredColumns) = (result.Metadata, result.StructuredColumns);
            }
            else
            {
                metadata = await reader.ReadAsync(borrowed, options, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) when (exception is DbException or InvalidOperationException or TimeoutException or NotSupportedException)
        { return Failure("SDI002", MetadataCoverage.Inaccessible, "Database metadata could not be accessed."); }
        cancellationToken.ThrowIfCancellationRequested();
        return Map(metadata, options, cancellationToken, reader as IUniqueConstraintMetadataReader, structuredColumns);
    }

    public SchemaInspectionResult Map(SchemaMetadata metadata, DiscoveryOptions? options = null, CancellationToken cancellationToken = default)
        => Map(metadata, options, cancellationToken, null);

    public SchemaInspectionResult Map(SchemaMetadata metadata, DiscoveryOptions? options,
        CancellationToken cancellationToken, IUniqueConstraintMetadataReader? uniqueMetadata)
        => Map(metadata, options, cancellationToken, uniqueMetadata, null);

    public SchemaInspectionResult Map(SchemaMetadata metadata, DiscoveryOptions? options,
        CancellationToken cancellationToken, IUniqueConstraintMetadataReader? uniqueMetadata,
        IReadOnlyDictionary<StructuredColumnIdentity, StructuredColumnMetadata>? structuredMetadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        cancellationToken.ThrowIfCancellationRequested();
        try { return MapCore(metadata, options ?? new(), cancellationToken, uniqueMetadata, structuredMetadata); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException)
        { return Failure("SDI007", MetadataCoverage.Unrepresentable, "Metadata could not be mapped into a valid schema."); }
    }

    private static SchemaInspectionResult MapCore(SchemaMetadata metadata, DiscoveryOptions options,
        CancellationToken cancellationToken, IUniqueConstraintMetadataReader? uniqueMetadata,
        IReadOnlyDictionary<StructuredColumnIdentity, StructuredColumnMetadata>? structuredMetadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        options ??= new();
        var diagnostics = new List<DiscoveryDiagnostic>();
        var coverage = options.Features.Concat(new[] { SchemaFeature.Tables, SchemaFeature.Columns }).Distinct().ToDictionary(feature => feature,
            feature => metadata.Coverage.GetValueOrDefault(feature, MetadataCoverage.Unsupported));
        foreach (var item in coverage.Where(item => item.Value is not (MetadataCoverage.Available or MetadataCoverage.Absent)))
            diagnostics.Add(new("SDI003", item.Key, item.Value, "Requested metadata is not completely available."));
        var selected = SelectTables(metadata, options, diagnostics);
        diagnostics.AddRange(metadata.Diagnostics.Where(item => coverage.ContainsKey(item.Feature) && (item.Object is null || selected.ContainsKey(item.Object))));
        var tables = new List<Table>();
        var indexes = new List<Index>();
        foreach (var table in selected.Values.OrderBy(table => Name(table.Identity).Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var unique = table.UniqueConstraints.OrderBy(key => key.Name, StringComparer.Ordinal)
                    .Select(key => (Key: key, Metadata: uniqueMetadata?.GetUniqueConstraintMetadata(table, key))).ToArray();
                var mapped = MapTable(table, selected, diagnostics, unique, options.Features.Contains(SchemaFeature.Defaults), structuredMetadata);
                if (mapped is null)
                {
                    ApplyDiagnosticCoverage(coverage, diagnostics);
                    return new(null, false, false, coverage, diagnostics.ToArray());
                }
                tables.Add(mapped);
                indexes.AddRange(MapIndexes(table, unique, diagnostics));
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or KeyNotFoundException or NotSupportedException)
            {
                return new(null, false, false, coverage, diagnostics.Append(new DiscoveryDiagnostic("SDI007",
                    SchemaFeature.Tables, MetadataCoverage.Unrepresentable, "Metadata could not be mapped without losing semantics.", table.Identity)).ToArray());
            }
        }
        var views = MapViews(metadata, options, selected, diagnostics);
        return CompleteResult(metadata, options, diagnostics, coverage, tables, indexes, views);
    }

    private static SchemaInspectionResult CompleteResult(SchemaMetadata metadata, DiscoveryOptions options,
        List<DiscoveryDiagnostic> diagnostics, Dictionary<SchemaFeature, MetadataCoverage> coverage,
        List<Table> tables, List<Index> indexes, List<View> views)
    {
        try
        {
            var schema = new Schema(tables.ToArray(), indexes.ToArray(), views.ToArray());
            ApplyDiagnosticCoverage(coverage, diagnostics);
            var complete = diagnostics.Count == 0;
            return new(schema, complete, complete || options.AllowPartial, coverage, diagnostics) { ViewInventory = metadata.Views.Where(view => options.Tables.Count == 0 && MatchesScope(options, view.Identity)).ToArray() };
        }
        catch (ArgumentException)
        {
            return new(null, false, false, coverage, diagnostics.Append(new DiscoveryDiagnostic("SDI007", SchemaFeature.Tables,
            MetadataCoverage.Unrepresentable, "The discovered relationships do not form a valid schema.")).ToArray());
        }
    }

    private static Dictionary<MetadataName, MetadataTable> SelectTables(SchemaMetadata metadata, DiscoveryOptions options, List<DiscoveryDiagnostic> diagnostics)
    {
        var all = metadata.Tables.ToDictionary(table => table.Identity);
        var selected = metadata.Tables.Where(table =>
            MatchesScope(options, table.Identity)
            && (options.Tables.Count == 0 || options.Tables.Any(filter => Matches(filter, table.Identity)))).ToDictionary(table => table.Identity);
        foreach (var filter in options.Tables.Where(filter => !selected.Keys.Any(name => Matches(filter, name))))
            diagnostics.Add(new("SDI004", SchemaFeature.Tables, MetadataCoverage.Absent, "A selected table was not found or is not visible.", filter));
        if (options.IncludeReferencedTables) IncludeReferences(selected, all);
        return selected;
    }

    private static void IncludeReferences(Dictionary<MetadataName, MetadataTable> selected, Dictionary<MetadataName, MetadataTable> all)
    {
        var queue = new Queue<MetadataTable>(selected.Values);
        while (queue.TryDequeue(out var table))
        {
            foreach (var key in table.ForeignKeys)
            {
                if (!selected.ContainsKey(key.Target) && all.TryGetValue(key.Target, out var target))
                { selected.Add(target.Identity, target); queue.Enqueue(target); }
            }
        }
    }

    private static Table? MapTable(MetadataTable table, Dictionary<MetadataName, MetadataTable> selected,
        List<DiscoveryDiagnostic> diagnostics, (MetadataKey Key, UniqueConstraintMetadata? Metadata)[] uniqueKeys,
        bool inspectDefaults, IReadOnlyDictionary<StructuredColumnIdentity, StructuredColumnMetadata>? structuredMetadata)
    {
        if (table.Columns.Count == 0) throw new NotSupportedException();
        var columns = new List<Column>();
        foreach (var column in table.Columns.OrderBy(column => column.Ordinal))
        {
            var structured = structuredMetadata?.GetValueOrDefault(new StructuredColumnIdentity(table.Identity, column.Name));
            var mapped = structured is null ? MapColumn(table.Identity, column, diagnostics, inspectDefaults)
                : MapStructuredColumn(table.Identity, column, structured, diagnostics, inspectDefaults);
            if (mapped is null) return null;
            columns.Add(mapped);
        }
        var byName = columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
        var constraints = new List<Constraint>();
        if (table.PrimaryKey is { } pk)
            constraints.Add(new PrimaryKeyConstraint(pk.Columns.Select(name => byName[name]).ToArray(), pk.Name));
        constraints.AddRange(MapForeignKeys(table, selected, diagnostics));
        constraints.AddRange(table.Checks.OrderBy(check => check.Name, StringComparer.Ordinal).Select(check => new CheckConstraint(
            Expression(check.Left), check.Operator, Expression(check.Right), check.Name)));
        constraints.AddRange(uniqueKeys.Select(unique => new UniquenessConstraint(
            unique.Key.Columns.Select(name => byName[name]).ToArray(),
            unique.Key.Name, unique.Metadata?.NullPolicy ?? UniqueNullPolicy.Native)));

        return new Table(Name(table.Identity), columns.ToArray(), constraints.ToArray(), table.Comment, null, table.Annotations);
    }

    private static IEnumerable<Index> MapIndexes(MetadataTable table,
        (MetadataKey Key, UniqueConstraintMetadata? Metadata)[] uniqueKeys, List<DiscoveryDiagnostic> diagnostics)
    {
        var backing = uniqueKeys.SelectMany(key => key.Metadata?.BackingIndexes ?? [])
            .GroupBy(identity => identity).ToDictionary(group => group.Key, group => group.Count());
        foreach (var index in table.Indexes.OrderBy(index => Name(index.Identity).Key, StringComparer.Ordinal))
        {
            var claims = backing.GetValueOrDefault(index.Identity);
            if (claims == 1 && index.Unique)
                continue;
            if (claims > 0 || (index.Unique && uniqueKeys.Any(key =>
                key.Metadata is { NullPolicy: not UniqueNullPolicy.Native }
                && key.Key.Columns.ToHashSet(StringComparer.Ordinal).SetEquals(index.Columns.Select(column => column.Name)))))
            {
                diagnostics.Add(new("SDI010", SchemaFeature.Indexes, MetadataCoverage.Unrepresentable,
                    "A discovered index has ambiguous ownership or null semantics for a UNIQUE constraint.", table.Identity));
            }
            yield return new Index(Name(index.Identity), Name(table.Identity), index.Columns.Select(column =>
                new IndexColumn(column.Name, column.Descending is null ? null :
                    column.Descending.Value ? IndexSortDirection.Descending : IndexSortDirection.Ascending)).ToArray(), index.Unique);
        }
    }

    private static List<Constraint> MapForeignKeys(MetadataTable table, Dictionary<MetadataName, MetadataTable> selected, List<DiscoveryDiagnostic> diagnostics)
    {
        var constraints = new List<Constraint>();
        foreach (var fk in table.ForeignKeys.OrderBy(key => key.Name, StringComparer.Ordinal))
        {
            if (!selected.ContainsKey(fk.Target))
            {
                diagnostics.Add(new("SDI005", SchemaFeature.ForeignKeys, MetadataCoverage.Unrepresentable,
                    "A foreign-key target is outside the selected or visible tables; the key was omitted.", table.Identity));
                continue;
            }
            constraints.Add(new ForeignKeyConstraint(fk.Name, fk.Columns, Name(fk.Target), fk.TargetColumns));
        }
        return constraints;
    }

    private static List<View> MapViews(SchemaMetadata metadata, DiscoveryOptions options, Dictionary<MetadataName, MetadataTable> selected, List<DiscoveryDiagnostic> diagnostics)
    {
        var views = new List<View>();
        foreach (var view in metadata.Views.Where(_ => options.Features.Contains(SchemaFeature.Views)).OrderBy(view => Name(view.Identity).Key, StringComparer.Ordinal))
        {
            if (!MatchesScope(options, view.Identity)) continue;
            if (options.Tables.Count > 0) continue; // Table filters select tables, not views.
            if (view.Source is not null && (selected.ContainsKey(view.Source) || metadata.Views.Any(candidate => candidate.Identity == view.Source)) && view.Projections is not null)
            {
                views.Add(new View(Name(view.Identity), new ViewQuery(Name(view.Source),
                    view.Projections.Select(expression => new ViewProjection(Expression(expression.Expression), expression.Alias)).ToArray())));
            }
            else
            {
                diagnostics.Add(new("SDI008", SchemaFeature.Views, MetadataCoverage.Unrepresentable,
                "The view definition cannot be represented by a portable projection query.", view.Identity));
            }
        }
        return views;
    }

    private static void ApplyDiagnosticCoverage(Dictionary<SchemaFeature, MetadataCoverage> coverage, List<DiscoveryDiagnostic> diagnostics)
    {
        foreach (var group in diagnostics.GroupBy(item => item.Feature))
        {
            if (coverage.ContainsKey(group.Key))
            {
                coverage[group.Key] = group.Any(item => item.Coverage == MetadataCoverage.Inaccessible)
                    ? MetadataCoverage.Inaccessible : group.Any(item => item.Coverage == MetadataCoverage.Unrepresentable)
                        ? MetadataCoverage.Unrepresentable : group.First().Coverage;
            }
        }
    }

    private static NativeDatabaseType? MapNativeType(MetadataColumn column)
    {
        NativeDatabaseType? native = null;
        if (column.NativeType is not null)
        {
            if (column.NativeDialect is null) throw new NotSupportedException();
            native = new NativeDatabaseType(column.NativeType, column.NativeDialect);
        }
        return native;
    }

    private static ColumnGeneration? MapGeneration(MetadataColumn column)
    {
        return column.Generation is not { } g ? null : g.Expression is not null
            ? new GeneratedColumnGeneration(Expression(g.Expression), g.Stored ? GeneratedColumnStorage.Stored : GeneratedColumnStorage.Virtual)
            : new IdentityColumnGeneration(g.Seed ?? 1, g.Increment ?? 1);
    }

    private static Column MapColumn(MetadataName table, MetadataColumn column,
        List<DiscoveryDiagnostic> diagnostics, bool inspectDefaults)
    {
        if (column.Default is ColumnDefaultExpression expression && inspectDefaults)
        {
            try { return MapColumn(column, null, expression); }
            catch (ArgumentException)
            {
                diagnostics.Add(new("SDI011", SchemaFeature.Defaults, MetadataCoverage.Unrepresentable,
                    $"The discovered default for column '{column.Name}' is incompatible with its destination.", table));
            }
        }
        if (column.Default is MetadataExpression && inspectDefaults)
        {
            diagnostics.Add(new("SDI011", SchemaFeature.Defaults, MetadataCoverage.Unrepresentable,
                $"The catalog default for column '{column.Name}' cannot be represented as a typed default.", table));
        }

        return MapColumn(column, column.Default is ColumnDefaultExpression or MetadataExpression ? null : column.Default);
    }

    private static Column? MapStructuredColumn(MetadataName table, MetadataColumn column,
        StructuredColumnMetadata metadata, List<DiscoveryDiagnostic> diagnostics, bool inspectDefaults)
    {
        var label = $"The structured type for column '{column.Name}'";
        if (metadata.Type is not { } type)
        {
            var coverage = metadata.Coverage == MetadataCoverage.Available ? MetadataCoverage.Unrepresentable : metadata.Coverage;
            diagnostics.Add(new("SDI012", SchemaFeature.Columns, coverage, $"{label} has unknown shape or storage.", table));
            return null;
        }
        if (column.Nullable is null || column.Generation is { Expression: null })
        {
            diagnostics.Add(new("SDI012", SchemaFeature.Columns, MetadataCoverage.Unrepresentable,
                $"{label} has unknown column nullability or incompatible generation.", table));
            return null;
        }
        if (metadata.Coverage != MetadataCoverage.Available || type.Shape.HasUnknownNullability)
        {
            var coverage = metadata.Coverage == MetadataCoverage.Available ? MetadataCoverage.Unrepresentable : metadata.Coverage;
            diagnostics.Add(new("SDI012", SchemaFeature.Columns, coverage,
                $"{label} has incomplete metadata.", table));
        }
        var sqlNull = column.Default is DBNull && column.Nullable is true && column.Generation is null;
        if (inspectDefaults && column.Default is not null && !sqlNull)
        {
            diagnostics.Add(new("SDI011", SchemaFeature.Defaults, MetadataCoverage.Unrepresentable,
                $"The catalog default for structured column '{column.Name}' cannot be represented.", table));
        }
        return new Column(column.Name, type, sqlNull ? DBNull.Value : null,
            [column.Nullable.Value ? new NullableConstraint() : new NotNullableConstraint()],
            column.Comment, MapGeneration(column) as GeneratedColumnGeneration)
        { Annotations = column.Annotations };
    }

    private static Column MapColumn(MetadataColumn column, object? defaultValue, ColumnDefaultExpression? expression = null)
    {
        if (column.Type is null || column.Nullable is null) throw new NotSupportedException();
        var constraints = new IConstraint[] { column.Nullable.Value ? new NullableConstraint() : new NotNullableConstraint() };
        var native = MapNativeType(column);
        var generation = MapGeneration(column);
        var type = column.Type.Value;
        return column.Length is { } length
            ? new VarLengthColumn(column.Name, type, length, defaultValue, constraints, column.Comment, native,
                generation: generation, defaultExpression: expression) { Annotations = column.Annotations }
            : column.Precision is { } precision
                ? new NumericColumn(column.Name, type, precision, column.Scale ?? 0, defaultValue, constraints, column.Comment, native,
                    generation: generation, defaultExpression: expression) { Annotations = column.Annotations }
                : new Column(column.Name, type, defaultValue, constraints, column.Comment, native,
                    generation: generation, defaultExpression: expression) { Annotations = column.Annotations };
    }

    private static Expression Expression(MetadataExpression expression) => expression.Kind switch
    {
        "column" => new ColumnIdentityExpression(expression.Name!),
        "value" => new ValueExpression(expression.Value!),
        "function" => new FunctionExpression(expression.Name!, (expression.Arguments ?? []).Select(Expression).ToArray()),
        _ => throw new NotSupportedException("Unsupported metadata expression.")
    };
    private static DatabaseObjectName Name(MetadataName name) => new(name.Name, name.Schema, name.Database, name.Catalog);
    private static bool MatchesScope(DiscoveryOptions options, MetadataName name) =>
        (options.Schemas.Count == 0 || options.Schemas.Contains(name.Schema, StringComparer.Ordinal))
        && (options.Databases.Count == 0 || options.Databases.Contains(name.Database, StringComparer.Ordinal))
        && (options.Catalogs.Count == 0 || options.Catalogs.Contains(name.Catalog, StringComparer.Ordinal));
    private static bool Matches(MetadataName filter, MetadataName name) =>
        filter.Name == name.Name && (filter.Schema is null || filter.Schema == name.Schema)
        && (filter.Database is null || filter.Database == name.Database) && (filter.Catalog is null || filter.Catalog == name.Catalog);
    private static SchemaInspectionResult Failure(string code, MetadataCoverage status, string message) =>
        new(null, false, false, new Dictionary<SchemaFeature, MetadataCoverage>(),
            [new(code, SchemaFeature.Tables, status, message)]);
}
