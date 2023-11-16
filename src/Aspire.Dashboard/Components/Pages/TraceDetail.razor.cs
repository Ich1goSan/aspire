// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using Aspire.Dashboard.Model;
using Aspire.Dashboard.Model.Otlp;
using Aspire.Dashboard.Otlp.Model;
using Aspire.Dashboard.Otlp.Storage;
using Microsoft.AspNetCore.Components;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Aspire.Dashboard.Components.Pages;

public partial class TraceDetail
{
    private OtlpTrace? _trace;
    private OtlpSpan? _span;
    private Subscription? _tracesSubscription;
    private List<SpanWaterfallViewModel>? _spanWaterfallViewModels;
    private int _maxDepth;

    [Parameter]
    public required string TraceId { get; set; }

    [Parameter]
    public string? SpanId { get; set; }

    [Inject]
    public required IDialogService DialogService { get; set; }

    [Inject]
    public required TelemetryRepository TelemetryRepository { get; set; }

    private ValueTask<GridItemsProviderResult<SpanWaterfallViewModel>> GetData(GridItemsProviderRequest<SpanWaterfallViewModel> request)
    {
        Debug.Assert(_spanWaterfallViewModels != null);

        return ValueTask.FromResult(new GridItemsProviderResult<SpanWaterfallViewModel>
        {
            Items = _spanWaterfallViewModels,
            TotalItemCount = _spanWaterfallViewModels.Count
        });
    }

    private static List<SpanWaterfallViewModel> CreateSpanWaterfallViewModels(OtlpTrace trace)
    {
        var orderedSpans = new List<SpanWaterfallViewModel>();
        // There should be one root span but just in case, we'll add them all.
        foreach (var rootSpan in trace.Spans.Where(s => string.IsNullOrEmpty(s.ParentSpanId)).OrderBy(s => s.StartTime))
        {
            AddSelfAndChildren(orderedSpans, rootSpan, depth: 1, CreateViewModel);
        }
        // Unparented spans.
        foreach (var unparentedSpan in trace.Spans.Where(s => !string.IsNullOrEmpty(s.ParentSpanId) && s.GetParentSpan() == null).OrderBy(s => s.StartTime))
        {
            AddSelfAndChildren(orderedSpans, unparentedSpan, depth: 1, CreateViewModel);
        }

        return orderedSpans;

        static void AddSelfAndChildren(List<SpanWaterfallViewModel> orderedSpans, OtlpSpan span, int depth, Func<OtlpSpan, int, SpanWaterfallViewModel> createViewModel)
        {
            orderedSpans.Add(createViewModel(span, depth));
            depth++;

            foreach (var child in span.GetChildSpans().OrderBy(s => s.StartTime))
            {
                AddSelfAndChildren(orderedSpans, child, depth, createViewModel);
            }
        }

        static SpanWaterfallViewModel CreateViewModel(OtlpSpan span, int depth)
        {
            var traceStart = span.Trace.FirstSpan.StartTime;
            var relativeStart = span.StartTime - traceStart;
            var rootDuration = span.Trace.Duration.TotalMilliseconds;

            var leftOffset = relativeStart.TotalMilliseconds / rootDuration * 100;
            var width = span.Duration.TotalMilliseconds / rootDuration * 100;

            // Figure out if the label is displayed to the left or right of the span.
            // If the label position is based on whether more than half of the span is on the left or right side of the trace.
            var labelIsRight = (relativeStart + span.Duration / 2) < (span.Trace.Duration / 2);

            // A span may indicate a call to another service but the service isn't instrumented.
            var hasPeerService = span.Attributes.Any(a => a.Key == OtlpSpan.PeerServiceAttributeKey);
            var isUninstrumentedPeer = hasPeerService && span.Kind is OtlpSpanKind.Client or OtlpSpanKind.Producer && !span.GetChildSpans().Any();

            var viewModel = new SpanWaterfallViewModel
            {
                Span = span,
                LeftOffset = leftOffset,
                Width = width,
                Depth = depth,
                LabelIsRight = labelIsRight,
                UninstrumentedPeer = isUninstrumentedPeer ? OtlpHelpers.GetValue(span.Attributes, OtlpSpan.PeerServiceAttributeKey) : null
            };
            return viewModel;
        }
    }

    protected override void OnParametersSet()
    {
        UpdateDetailViewData();
    }

    private void UpdateDetailViewData()
    {
        _trace = null;
        _span = null;

        if (TraceId is not null)
        {
            _trace = TelemetryRepository.GetTrace(TraceId);
            if (_trace != null)
            {
                _spanWaterfallViewModels = CreateSpanWaterfallViewModels(_trace);
                _maxDepth = _spanWaterfallViewModels.Max(s => s.Depth);

                if (_tracesSubscription is null || _tracesSubscription.ApplicationId != _trace.FirstSpan.Source.InstanceId)
                {
                    _tracesSubscription?.Dispose();
                    _tracesSubscription = TelemetryRepository.OnNewTraces(_trace.FirstSpan.Source.InstanceId, SubscriptionType.Read, () => InvokeAsync(() =>
                    {
                        UpdateDetailViewData();
                        StateHasChanged();
                        return Task.CompletedTask;
                    }));
                }
                if (SpanId is not null)
                {
                    _span = _trace.Spans.FirstOrDefault(s => s.SpanId.StartsWith(SpanId, StringComparison.Ordinal));
                }
            }
        }
    }

    private string GetRowClass(SpanWaterfallViewModel viewModel)
    {
        return (viewModel.Span.SpanId == _span?.SpanId) ? "selected-span" : string.Empty;
    }

    public SpanDetailsViewModel? SelectedSpan { get; set; }

    private void OnShowProperties(SpanWaterfallViewModel viewModel)
    {
        if (SelectedSpan?.Span == viewModel.Span)
        {
            ClearSelectedSpan();
        }
        else
        {
            var entryProperties = viewModel.Span.AllProperties()
                .Select(kvp => new SpanPropertyViewModel { Name = kvp.Key, Value = kvp.Value })
                .ToList();

            var spanDetailsViewModel = new SpanDetailsViewModel
            {
                Span = viewModel.Span,
                Properties = entryProperties,
                Title = $"{viewModel.Span.Source.ApplicationName}: {viewModel.GetDisplaySummary()} {OtlpHelpers.ToShortenedId(viewModel.Span.SpanId)}"
            };

            SelectedSpan = spanDetailsViewModel;
        }
    }

    private void ClearSelectedSpan()
    {
        SelectedSpan = null;
    }

    public void Dispose()
    {
        _tracesSubscription?.Dispose();
    }
}
