using System;
class RecoveryTests {
    static void Require(bool value,string message) {if(!value) throw new Exception(message);}
    static Usage Sample(double five,double week) {return new Usage {Five=new WindowUsage {Remaining=five},Week=new WindowUsage {Remaining=week}};}
    static void Main() {
        try {Run();}
        catch(Exception error) {Console.Error.WriteLine("FAIL: "+error.Message);Environment.ExitCode=1;}
    }
    static void Run() {
        var now=DateTime.UtcNow;var c=new RecoveryControl {Enabled=true};
        RecoveryPolicy.Observe(c,Sample(70,90),now);Require(c.RecoveryId==null,"Normal usage must not trigger resume.");
        RecoveryPolicy.Observe(c,Sample(0,90),now);Require(c.WaitingForAllowance,"Exhaustion must arm resume.");Require(c.BlockedSinceUtc==now,"Record outage start.");
        RecoveryPolicy.Observe(c,Sample(0,80),now.AddMinutes(2));Require(c.BlockedSinceUtc==now,"Do not move outage start.");
        RecoveryPolicy.Observe(c,Sample(100,0),now.AddHours(5));Require(c.WaitingForAllowance && c.RecoveryId==null,"Weekly exhaustion must still block.");
        RecoveryPolicy.Observe(c,Sample(100,100),now.AddDays(7));Require(!c.WaitingForAllowance && c.RecoveryId!=null,"Recovery must trigger one request.");
        string first=c.RecoveryId;RecoveryPolicy.Observe(c,Sample(98,99),now.AddDays(7).AddMinutes(1));Require(c.RecoveryId==first,"Polling must not duplicate a request.");
        c.Enabled=false;RecoveryPolicy.Observe(c,Sample(0,99),now);Require(!c.WaitingForAllowance,"Disabled mode must not arm.");
        c.Enabled=true;RecoveryPolicy.Observe(c,Sample(99,0),now.AddDays(8));Require(c.WaitingForAllowance && c.RecoveryId==null,"A new outage clears old request.");
        RecoveryPolicy.Observe(c,new Usage {Five=new WindowUsage {Remaining=99}},now);Require(c.WaitingForAllowance,"Missing weekly data cannot establish recovery.");
        var json=new System.Web.Script.Serialization.JavaScriptSerializer();var restored=json.Deserialize<RecoveryControl>("{\"Enabled\":false,\"RunningUntilUtc\":\"2026-10-24T00:00:00.0000000Z\"}");Require(!restored.Enabled && restored.RunningUntilUtc.ToUniversalTime()==new DateTime(2026,10,24,0,0,0,DateTimeKind.Utc),"Persisted switch and timestamps must reload.");
        Console.WriteLine("PASS: normal usage, exhaustion, weekly block, recovery, deduplication, disabled mode, new outage, missing data, persistence.");
    }
}
