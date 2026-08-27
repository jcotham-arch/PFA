using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.OrderFlow;
using PFA_FVG_Scanner.Domain.Sessions;

namespace PFA_FVG_Scanner.Services;

public sealed class OrderFlowService
{
    private readonly OrderFlowCanonicalizer _canonicalizer;private readonly TradeAggressorClassifier _classifier;private readonly OrderFlowFeatureEngine _features;private readonly OrderFlowRepository _repository;private readonly ITradingSessionService _sessions;
    public OrderFlowService(OrderFlowCanonicalizer canonicalizer,TradeAggressorClassifier classifier,OrderFlowFeatureEngine features,OrderFlowRepository repository,ITradingSessionService sessions){_canonicalizer=canonicalizer;_classifier=classifier;_features=features;_repository=repository;_sessions=sessions;}
    public async Task<OrderFlowCanonicalizationBatch> IngestAsync(IReadOnlyList<ProviderOrderFlowEvent> source,CancellationToken token=default)
    {var targets=source.Where(x=>x.CorrectsProviderEventId is not null).Select(x=>(x.Provider,x.CorrectsProviderEventId!)).ToArray();var known=await _repository.FindProviderEventsAsync(targets,token);var batch=_canonicalizer.Canonicalize(source,known);await _repository.SaveEventsAsync(batch.Accepted,token);return batch;}
    public async Task<OrderFlowFeatureSnapshot> BuildSnapshotAsync(string instrumentId,string? contractId,DateTime startUtc,DateTime endUtc,decimal priceIncrement,DateTime asOfUtc,string dataRevision,CancellationToken token=default)
    {var session=_sessions.Assign(instrumentId,startUtc);var events=await _repository.GetEventsAsync(instrumentId,session.Session.SessionOpenUtc,endUtc,asOfUtc,token);var trades=_classifier.Classify(events,asOfUtc,dataRevision);await _repository.SaveClassificationsAsync(trades,token);var snapshot=_features.Build(instrumentId,contractId,startUtc,endUtc,priceIncrement,asOfUtc,dataRevision,events,trades);await _repository.SaveSnapshotAsync(snapshot,token);return snapshot;}
}
