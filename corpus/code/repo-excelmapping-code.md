# ExcelMapping code examples (htg-server)

Three excerpts from HTGPlus.ExcelMapping, copied verbatim from git at commit e570f4474d (full hash e570f4474dd499abcdc99292d0db31332ebd0a62, committed 2026-10-28). Each section gives the file path, the commit and the exact line range. They show how extracted worksheet data is mapped to HTG timecard fields.

## Example 1 — TraditionalExtractor.ExtractDataAsync: extracts a Traditional file inside htg-server (H2G-7382)

- Path: `HTGPlus.ExcelMapping/DataIngestionMapping/TraditionalDataIngestion/TraditionalExtractor.cs`
- Commit: `e570f4474d`
- Lines: 16-58
- Link: https://github.com/cast-and-crew/htg-server/blob/e570f4474dd499abcdc99292d0db31332ebd0a62/HTGPlus.ExcelMapping/DataIngestionMapping/TraditionalDataIngestion/TraditionalExtractor.cs#L16-L58

```csharp
    public async Task<Result<List<DataExtractionResponse>>> ExtractDataAsync(ProcessFileRequest request
        , Stream file
        , TemplateInfo templateInfo
        , CancellationToken cancellationToken = default)
    {
        try
        {
            ResetPosition(file);

            var packages = (await fileExtractionService.ExtractDataAsync(file, request.FileName, [templateInfo.ToReaderTemplate()], cancellationToken)).ToList();

            if (packages.Count == 0)
            {
                logger.LogWarning("[TraditionalExtractor] Worksheet {Worksheet} was not found in file {FileKey} ({FileName}) for template {TemplateId}.",
                    templateInfo.ForWorksheet, request.FileKey, request.FileName, templateInfo.Id);

                return Error.Failure(description: WorksheetNotFoundMessage);
            }

            foreach (var package in packages)
            {
                package.Filename = request.FileName;
                package.EorId = GetMetadataText(request, "eorID");
                package.ParentClientId = GetMetadataText(request, "parentClientID");
                package.ClientId = GetMetadataText(request, "clientID");
            }

            if (packages.FirstOrDefault()?.Timecards?.Count is null or 0)
            {
                logger.LogInformation("[TraditionalExtractor] No timecards extracted from file {FileKey} ({FileName}) worksheet {Worksheet} template {TemplateId}.",
                    request.FileKey, request.FileName, templateInfo.ForWorksheet, templateInfo.Id);
            }

            return packages.ToExtractionResponse();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "[TraditionalExtractor] Extraction failed for file {FileKey} ({FileName}) worksheet {Worksheet} template {TemplateId}.",
                request.FileKey, request.FileName, templateInfo.ForWorksheet, templateInfo.Id);

            return Error.Failure(description: ExtractionFailedMessage);
        }
    }
```

## Example 2 — PayTypeOverrideExtractionMapping: maps the template column that holds the pay type override

- Path: `HTGPlus.ExcelMapping/DataIngestionMapping/Mappers/PayTypeOverrideExtractionMapping.cs`
- Commit: `e570f4474d`
- Lines: 16-60
- Link: https://github.com/cast-and-crew/htg-server/blob/e570f4474dd499abcdc99292d0db31332ebd0a62/HTGPlus.ExcelMapping/DataIngestionMapping/Mappers/PayTypeOverrideExtractionMapping.cs#L16-L60

```csharp
    public void SetTemplateMappings(TemplateInfo templateInfo)
    {
        if (!string.IsNullOrWhiteSpace(_payTypeOverrideField)) return;

        var mapping = templateInfo.Mappings
            .FirstOrDefault(x => x.Field.Equals(RuleTableProperties<ImportTemplate>.GetResultColumn(m => m.PayTypeOverride!))
                && x.MapsTo?.Any(m => !string.IsNullOrWhiteSpace(m.Name)) is true );

        if (mapping is null) return;

        _payTypeOverrideField = mapping.MapsTo!.First(w => !string.IsNullOrWhiteSpace(w.Name)).Name!;
        _templateInfo = templateInfo;
        _payTypeOverrideSelectedColumn = _templateInfo.Mappings
            .FirstOrDefault(x => x.Field.Equals(_payTypeOverrideField, StringComparison.OrdinalIgnoreCase));
    }

    public void ApplyMapping(TimecardItem item)
    {
        // No implementation needed for TimecardItem
    }

    public void ApplyMapping(TimecardDistribution distributionItem, TimecardItem item)
    {
        if (string.IsNullOrWhiteSpace(_payTypeOverrideField)) return;

        if (_payTypeOverrideSelectedColumn is null)
        {
            distributionItem.PayTypeOverride = null;
            return;
        }

        if (TimecardPropertyAccessor.TimecardDistributionPropertyAccessors.TryGetValue(_payTypeOverrideField, out var accessor))
        {
            distributionItem.PayTypeOverride = accessor(distributionItem);
            return;
        }
        
        if (TimecardPropertyAccessor.TimecardPropertyAccessors.TryGetValue(_payTypeOverrideField, out var timecardAccessor))
        {
            distributionItem.PayTypeOverride = timecardAccessor(item);
            return;
        }

        distributionItem.PayTypeOverride = null;
    }
```

## Example 3 — BasePropertyMapping.MapAsync: copies extracted columns into the timecard distribution fields

- Path: `HTGPlus.ExcelMapping/DataIngestionMapping/Mappers/BasePropertyMapping.cs`
- Commit: `e570f4474d`
- Lines: 18-70
- Link: https://github.com/cast-and-crew/htg-server/blob/e570f4474dd499abcdc99292d0db31332ebd0a62/HTGPlus.ExcelMapping/DataIngestionMapping/Mappers/BasePropertyMapping.cs#L18-L70

```csharp
    public ValueTask MapAsync(MapContext context, TimecardDistribution result
        , TimecardDistributionBaggage baggage
        , CancellationToken cancellationToken = default)
    {
        result.OccupationCode = context.TimecardDistribution?.Occupation?.Trim();
        result.CostCodeDescription = context.TimecardDistribution?.CostCodeDescription?.Trim();
        result.PayTypeOverride = context.TimecardDistribution?.PayTypeOverride;
        result.EstimatedGross = context.TimecardDistribution?.EstimatedGross;
        result.DeductionOverride = context.TimecardDistribution?.DeductionOverride;
        result.Warnings = context.TimecardDistribution?.Warnings ?? Enumerable.Empty<string>();
        result.CPJN = context.TimecardDistribution?.CPJN?.Trim();
        result.Notes = context.TimecardDistribution?.Notes;
        result.TK2 = context.TimecardDistribution?.TK2;
        result.TK3 = context.TimecardDistribution?.TK3;
        result.TK4 = context.TimecardDistribution?.TK4;
        result.TK5 = context.TimecardDistribution?.TK5;
        result.TK6 = context.TimecardDistribution?.TK6;
        result.TK7 = context.TimecardDistribution?.TK7;
        result.TK8 = context.TimecardDistribution?.TK8;
        result.TK9 = context.TimecardDistribution?.TK9;
        result.TK10 = context.TimecardDistribution?.TK10;

        // Columns that might be overridden by static values provided by user
        DateTime? workStartDateFromStatic = baggage.TryGetFromStaticColumn<DateTime>(StaticColumn.WorkStartDate, out var workStartDate) ? workStartDate : null;
        DateTime? workEndDateFromStatic = baggage.TryGetFromStaticColumn<DateTime>(StaticColumn.WorkEndDate, out var workEndDate) ? workEndDate : null;

        result.WorkStartDate = workStartDateFromStatic is not null ?
            workStartDateFromStatic.GetDateTime() : (workEndDateFromStatic is not null ? workEndDateFromStatic.GetDateTime() : context.TimecardDistribution?.WorkStartDate.GetDateTime());

        result.WorkEndDate = workEndDateFromStatic is not null ?
            workEndDateFromStatic.GetDateTime() : (workStartDateFromStatic is not null ? workStartDateFromStatic.GetDateTime() : context.TimecardDistribution?.WorkEndDate.GetDateTime());

        result.TK1 = baggage.TryGetFromStaticColumn<string>(StaticColumn.CostCode, out var tk1) ?
            tk1 : context.TimecardDistribution?.TK1;

        //rateOverride mapping
        if (context.TimecardDistribution?.RateOverride is not null)
        {
            foreach (var rateOverride in context.TimecardDistribution!.RateOverride)
            {
                if (rateOverride == null)
                {
                    continue;
                }
                var rateOverrideItem = new RateOverrideItem
                {
                    Type = rateOverride.Type,
                    Value = rateOverride.Value
                };

                result.RateOverride.Add(rateOverrideItem);
            }
        }
```

