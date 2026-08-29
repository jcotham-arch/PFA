using System.Text.Json;
using Microsoft.Data.Sqlite;
using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.Domain.Agent;
using PFA_FVG_Scanner.Domain.Research;

namespace PFA_FVG_Scanner.Services;

public sealed record ExploratorySandboxAdmissionPolicy(string Version,string InstrumentId,int MinimumTrainResolved,
    int MinimumValidationResolved,decimal MinimumTrainMeanNetR,decimal MinimumValidationMeanNetR,
    decimal MinimumTrainProfitFactor,decimal MinimumValidationProfitFactor,
    bool TestPartitionMayInfluenceAdmission=false,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record ExploratorySandboxCandidate(string CandidateId,string PatternTradeRunId,string HypothesisId,
    string InstrumentId,string ModuleId,string DirectionPolicy,string EntryPolicy,string StopPolicy,string ExitPolicy,
    decimal TargetR,int MaximumHoldingMinutes,int TrainResolved,decimal TrainMeanNetR,decimal TrainProfitFactor,
    int ValidationResolved,decimal ValidationMeanNetR,decimal ValidationProfitFactor,string AdmissionStatus,
    string TestPartitionStatus="WithheldFromExploratorySelection",bool CanEnterExploratoryPaper=true,
    bool IsStatisticallyValidated=false,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false);

public sealed record ExploratorySandboxCandidateQueue(string Status,string InstrumentId,
    ExploratorySandboxAdmissionPolicy Policy,int RunsReviewed,int HypothesesReviewed,int DevelopmentRejected,
    IReadOnlyList<ExploratorySandboxCandidate> Candidates,IReadOnlyList<string> NextRequirements,
    bool IsProspectivePaperOnly=true,bool CanActivateStrategy=false,bool CanRouteToRealBroker=false,
    string Interpretation="Exploratory admission starts paper observation sooner; it does not satisfy certification, statistical validation, or live-trading gates.");

public sealed class ExploratorySandboxCandidateService(PfaDatabase database)
{
    public const string Version="mes-exploratory-sandbox-admission-1.0.0";
    public static readonly ExploratorySandboxAdmissionPolicy MesPolicy=new(Version,"MES",30,10,0m,0m,1m,1m);

    public async Task<ExploratorySandboxCandidateQueue> GetAsync(string instrumentId="MES",CancellationToken token=default)
    {
        instrumentId=instrumentId.Trim().ToUpperInvariant();
        if(instrumentId!="MES")throw new ArgumentException("The first exploratory sandbox lane is intentionally MES-only.");
        var runs=await Runs(token);var eligibleRuns=runs.Where(x=>x.InstrumentIds.Count>0&&
            x.InstrumentIds.All(id=>id.Equals("MES",StringComparison.OrdinalIgnoreCase))).ToArray();
        var reviewed=0;var candidates=new List<ExploratorySandboxCandidate>();
        foreach(var run in eligibleRuns.OrderByDescending(x=>x.CreatedAtUtc))
        foreach(var group in run.Summaries.GroupBy(x=>x.HypothesisId,StringComparer.Ordinal))
        {
            reviewed++;var train=group.SingleOrDefault(x=>x.Split=="Train");var validation=group.SingleOrDefault(x=>x.Split=="Validation");
            if(train is null||validation is null)continue;var trainResolved=Resolved(train);var validationResolved=Resolved(validation);
            if(trainResolved<MesPolicy.MinimumTrainResolved||validationResolved<MesPolicy.MinimumValidationResolved||
               train.MeanNetR<=MesPolicy.MinimumTrainMeanNetR||validation.MeanNetR<=MesPolicy.MinimumValidationMeanNetR||
               train.ProfitFactor<=MesPolicy.MinimumTrainProfitFactor||validation.ProfitFactor<=MesPolicy.MinimumValidationProfitFactor)continue;
            var seed=$"{Version}|{run.RunId}|{train.HypothesisId}";var id=$"ESC-{AgentTrainingDatasetBuilder.Hash(seed)[..32]}";
            candidates.Add(new(id,run.RunId,train.HypothesisId,"MES",train.ModuleId,train.DirectionPolicy.ToString(),
                train.EntryPolicy,train.StopPolicy,train.ExitPolicy,train.TargetR,train.MaximumHoldingMinutes,
                trainResolved,train.MeanNetR,train.ProfitFactor,validationResolved,validation.MeanNetR,
                validation.ProfitFactor,"EligibleForExploratoryPaper"));
        }
        var ranked=candidates.OrderByDescending(x=>Math.Min(x.TrainMeanNetR,x.ValidationMeanNetR))
            .ThenByDescending(x=>Math.Min(x.TrainProfitFactor,x.ValidationProfitFactor))
            .ThenBy(x=>x.CandidateId,StringComparer.Ordinal).Take(20).ToArray();
        var requirements=ranked.Length==0
            ?new[]{"Accumulate a MES-only hypothesis with at least 30 resolved training and 10 resolved validation trades.",
                "Require positive net expectancy and profit factor above 1.0 in both development partitions."}
            :new[]{"Freeze the complete hypothesis before observing any new market data.",
                "Connect a prospective MES market-data source and record feed coverage.",
                "Accumulate forward paper trades without using them to rewrite this candidate version.",
                "Certification still requires the separate full statistical survival policy."};
        return new(ranked.Length>0?"CandidatesReadyForExploratoryPaper":"NoDevelopmentCandidate","MES",MesPolicy,
            eligibleRuns.Length,reviewed,reviewed-candidates.Count,ranked,requirements);
    }

    private async Task<IReadOnlyList<PatternTradeResearchRun>> Runs(CancellationToken token)
    {
        await using var connection=database.CreateConnection();await connection.OpenAsync(token);
        await using var command=connection.CreateCommand();command.CommandText="SELECT RunJson FROM PatternTradeResearchRuns ORDER BY CreatedAtUtc DESC";
        var values=new List<PatternTradeResearchRun>();await using var reader=await command.ExecuteReaderAsync(token);
        while(await reader.ReadAsync(token))values.Add(JsonSerializer.Deserialize<PatternTradeResearchRun>(reader.GetString(0))!);
        return values;
    }
    private static int Resolved(PatternTradeHypothesisSummary value)=>value.Samples-value.Ambiguous-value.NoEntryOrInvalid;
}
