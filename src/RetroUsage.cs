using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class Program {
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        if (args.Contains("--check")) {
            try { var data = UsageClient.Read().GetAwaiter().GetResult(); File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"connection-check.txt"),data.Summary()); Environment.Exit(0); }
            catch(Exception ex) { File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"connection-check.txt"),ex.Message); Environment.Exit(1); }
            return;
        }
        bool created;
        using(var mutex = new Mutex(true,"Local\\RetroCodexUsageMeter",out created)) {
            if (!created) { MessageBox.Show("The usage meter is already running. Look for its amber tray icon.","Retro Usage"); return; }
            Application.Run(new Meter());
        }
    }
}

public class WindowUsage {
    public double Remaining {get;set;}
    public long Reset {get;set;}
}
public class Usage {
    public WindowUsage Five {get;set;}
    public WindowUsage Week {get;set;}
    public DateTime CheckedUtc {get;set;}
    public string Summary() { return "Five-hour remaining: "+(Five==null?"unavailable":Five.Remaining+"%")+Environment.NewLine+"Weekly remaining: "+(Week==null?"unavailable":Week.Remaining+"%")+Environment.NewLine+"Checked UTC: "+CheckedUtc.ToString("O"); }
}

public class RecoveryControl {
    public bool Enabled {get;set;}
    public bool WaitingForAllowance {get;set;}
    public DateTime RunningUntilUtc {get;set;}
    public DateTime BlockedSinceUtc {get;set;}
    public string RecoveryId {get;set;}
    public DateTime RecoveryDetectedUtc {get;set;}
}

static class RecoveryPolicy {
    public static bool Exhausted(Usage usage) {return usage!=null && ((usage.Five!=null && usage.Five.Remaining<=0) || (usage.Week!=null && usage.Week.Remaining<=0));}
    public static bool Available(Usage usage) {return usage!=null && usage.Five!=null && usage.Week!=null && usage.Five.Remaining>0 && usage.Week.Remaining>0;}
    public static void Observe(RecoveryControl control,Usage usage,DateTime now) {
        if(!control.Enabled) return;
        if(Exhausted(usage)) {
            if(!control.WaitingForAllowance) {control.BlockedSinceUtc=now;control.RecoveryId=null;}
            control.WaitingForAllowance=true;
        } else if(control.WaitingForAllowance && Available(usage)) {
            control.WaitingForAllowance=false;
            control.RecoveryId=Guid.NewGuid().ToString("D");
            control.RecoveryDetectedUtc=now;
        }
    }
}

static class UsageClient {
    static Dictionary<string,object> Obj(object value) { return value as Dictionary<string,object>; }
    static object Get(Dictionary<string,object> value,string key) { object result; return value!=null && value.TryGetValue(key,out result)?result:null; }
    static WindowUsage ParseWindow(object raw,int minutes) {
        var item=Obj(raw);
        if(item==null || Get(item,"usedPercent")==null || Get(item,"resetsAt")==null || Convert.ToInt32(Get(item,"windowDurationMins"))!=minutes) return null;
        return new WindowUsage { Remaining=Math.Max(0,Math.Min(100,100-Convert.ToDouble(item["usedPercent"]))), Reset=Convert.ToInt64(item["resetsAt"]) };
    }
    static string FindCodex() {
        var local=Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var bundled=Path.Combine(local,"OpenAI","Codex","bin");
        if(Directory.Exists(bundled)) {
            var found=Directory.GetFiles(bundled,"codex.exe",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault();
            if(found!=null) return found;
        }
        foreach(string folder in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) {
            try { string file=Path.Combine(folder.Trim('"'),"codex.exe"); if(File.Exists(file)) return file; } catch { }
        }
        throw new Exception("Codex not found. Install or update the Codex desktop app.");
    }
    static async Task<Dictionary<string,object>> Response(Process process,int id,JavaScriptSerializer json) {
        while(true) {
            var line=await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
            if(line==null) throw new Exception("Codex connection closed. Check your Codex sign-in.");
            Dictionary<string,object> message;
            try { message=json.Deserialize<Dictionary<string,object>>(line); } catch { continue; }
            if(Get(message,"id")==null || Convert.ToString(message["id"])!=id.ToString()) continue;
            if(Get(message,"error")!=null) throw new Exception("Usage unavailable. Check your Codex sign-in and connection.");
            return Obj(Get(message,"result"));
        }
    }
    public static async Task<Usage> Read() {
        var info=new ProcessStartInfo(FindCodex(),"app-server --listen stdio://") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=AppDomain.CurrentDomain.BaseDirectory };
        using(var process=new Process {StartInfo=info}) {
            process.Start();
            process.ErrorDataReceived+=(s,e)=>{};
            process.BeginErrorReadLine();
            try {
                var query=Query(process);
                if(await Task.WhenAny(query,Task.Delay(20000)).ConfigureAwait(false)!=query) throw new Exception("Connection timed out. Retrying in one minute.");
                return await query.ConfigureAwait(false);
            } finally { try { if(!process.HasExited) process.Kill(); } catch { } }
        }
    }
    static async Task<Usage> Query(Process process) {
        var json=new JavaScriptSerializer();
        process.StandardInput.WriteLine("{\"id\":1,\"method\":\"initialize\",\"params\":{\"clientInfo\":{\"name\":\"retro_usage_meter\",\"title\":\"Retro Usage Meter\",\"version\":\"1.0.0\"}}}");
        process.StandardInput.Flush();
        await Response(process,1,json).ConfigureAwait(false);
        process.StandardInput.WriteLine("{\"method\":\"initialized\"}");
        process.StandardInput.WriteLine("{\"id\":2,\"method\":\"account/rateLimits/read\"}");
        process.StandardInput.Flush();
        var result=await Response(process,2,json).ConfigureAwait(false);
        var buckets=Obj(Get(result,"rateLimitsByLimitId"));
        var bucket=Obj(Get(buckets,"codex"))??Obj(Get(result,"rateLimits"));
        if(bucket==null || (Get(bucket,"limitId")!=null && Convert.ToString(bucket["limitId"])!="codex")) throw new Exception("Codex allowance unavailable for this account.");
        var five=ParseWindow(Get(bucket,"primary"),300)??ParseWindow(Get(bucket,"secondary"),300);
        var week=ParseWindow(Get(bucket,"primary"),10080)??ParseWindow(Get(bucket,"secondary"),10080);
        if(five==null && week==null) throw new Exception("This account did not return five-hour or weekly limits.");
        return new Usage { Five=five,Week=week,CheckedUtc=DateTime.UtcNow };
    }
}

class Meter : Form {
    readonly Color bg=Color.FromArgb(17,21,19), panel=Color.FromArgb(24,30,25), amber=Color.FromArgb(244,184,81), dim=Color.FromArgb(147,139,106), track=Color.FromArgb(52,57,40), red=Color.FromArgb(255,119,83);
    readonly string directory=AppDomain.CurrentDomain.BaseDirectory;
    readonly TimeZoneInfo zone=TimeZoneInfo.FindSystemTimeZoneById("AUS Eastern Standard Time");
    readonly NotifyIcon tray;
    readonly Button pin,refresh,autoResume;
    readonly System.Windows.Forms.Timer clock=new System.Windows.Forms.Timer(), poll=new System.Windows.Forms.Timer();
    Usage usage;
    // Chat recovery requires a separately configured Codex scheduled task.
    RecoveryControl recovery=new RecoveryControl {Enabled=false};
    DateTime lastLeaseUtc=DateTime.MinValue;
    bool busy,failed,quitting;
    string status="CONNECTING TO CODEX...",detail="";
    float scale=1;
    ToolTip tip=new ToolTip();
    [DllImport("user32.dll")] static extern bool ReleaseCapture();
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h,int msg,int w,int l);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    public Meter() {
        Text="Codex Retro Usage"; FormBorderStyle=FormBorderStyle.None; BackColor=bg; ForeColor=amber; DoubleBuffered=true; TopMost=true; AutoScaleMode=AutoScaleMode.None;
        using(var g=CreateGraphics()) scale=g.DpiX/96f;
        ClientSize=new Size((int)(352*scale),(int)(554*scale));
        StartPosition=FormStartPosition.Manual;
        var area=Screen.PrimaryScreen.WorkingArea; Location=new Point(area.Right-Width-24,area.Top+40);
        pin=AddButton("PIN: ON",212,12,92,25,()=>{TopMost=!TopMost; pin.Text=TopMost?"PIN: ON":"PIN: OFF"; SaveSettings();});
        AddButton("X",312,12,25,25,()=>Close());
        autoResume=AddButton("AUTO RESUME: ON",18,475,316,25,()=>{
            recovery.Enabled=!recovery.Enabled;
            if(!recovery.Enabled) {recovery.WaitingForAllowance=false;recovery.RecoveryId=null;}
            else RecoveryPolicy.Observe(recovery,usage,DateTime.UtcNow);
            UpdateRecoveryLabel();WriteRecoveryControl();
        });
        refresh=AddButton("REFRESH",18,515,148,25,async()=>await RefreshUsage());
        AddButton("TO TRAY",184,515,150,25,()=>Hide());
        tip.SetToolTip(pin,"Keep above other windows"); tip.SetToolTip(refresh,"Refresh now; automatic refresh every 60 seconds");
        tip.SetToolTip(this,"Drag the title to move. Reset times are Sydney time. Usage is account-wide.");
        tip.SetToolTip(autoResume,"After exhausted allowance recovers, Codex checks usage-stopped chats every 15 minutes. Keep Codex and this meter running. Click to disable.");
        using(var iconBitmap=new Bitmap(32,32)) {
            using(var g=Graphics.FromImage(iconBitmap)) { g.Clear(bg); using(var pen=new Pen(amber,4)) { g.DrawArc(pen,5,5,22,22,140,280); } using(var b=new SolidBrush(amber)) {g.FillRectangle(b,14,10,4,14);} }
            IntPtr h=iconBitmap.GetHicon(); using(var temporary=Icon.FromHandle(h)) Icon=(Icon)temporary.Clone(); DestroyIcon(h);
        }
        var menu=new ContextMenuStrip(); menu.Items.Add("Show meter",null,(s,e)=>ShowMeter()); menu.Items.Add("Refresh",null,async(s,e)=>await RefreshUsage()); menu.Items.Add("Exit",null,(s,e)=>Close());
        tray=new NotifyIcon {Icon=Icon,Text="Codex Retro Usage",Visible=true,ContextMenuStrip=menu}; tray.DoubleClick+=(s,e)=>ShowMeter();
        MouseDown+=(s,e)=>{ if(e.Button==MouseButtons.Left && e.Y<52*scale) {ReleaseCapture(); SendMessage(Handle,0xA1,2,0);} };
        LoadSettings(); LoadCache();LoadRecoveryControl();
        RecoveryPolicy.Observe(recovery,usage,DateTime.UtcNow);UpdateRecoveryLabel();WriteRecoveryControl();
        clock.Interval=1000; clock.Tick+=(s,e)=>{if((DateTime.UtcNow-lastLeaseUtc).TotalSeconds>=30) WriteRecoveryControl();Invalidate();}; clock.Start();
        poll.Interval=60000; poll.Tick+=async(s,e)=>await RefreshUsage(); poll.Start();
        Shown+=async(s,e)=>await RefreshUsage();
        FormClosing+=(s,e)=>{quitting=true; SaveSettings();WriteRecoveryControl();clock.Stop();poll.Stop();tray.Visible=false;tray.Dispose();tip.Dispose();};
        AccessibleName="Codex usage meter";
    }
    Button AddButton(string text,int x,int y,int w,int h,Action action) {
        var b=new Button {Text=text,Bounds=new Rectangle((int)(x*scale),(int)(y*scale),(int)(w*scale),(int)(h*scale)),FlatStyle=FlatStyle.Flat,BackColor=panel,ForeColor=amber,Font=new Font("Consolas",9*scale,FontStyle.Regular,GraphicsUnit.Point),Cursor=Cursors.Hand,UseVisualStyleBackColor=false};
        b.FlatAppearance.BorderColor=track; b.Click+=(s,e)=>action(); Controls.Add(b); return b;
    }
    void ShowMeter() { Show(); WindowState=FormWindowState.Normal; Activate(); }
    void LoadSettings() {
        try {
            var settings=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(directory,"settings.json")));
            TopMost=Convert.ToBoolean(settings["pinned"]);pin.Text=TopMost?"PIN: ON":"PIN: OFF";
            var location=new Point(Convert.ToInt32(settings["x"]),Convert.ToInt32(settings["y"]));
            var rect=new Rectangle(location,Size);
            if(Screen.AllScreens.Any(s=>s.WorkingArea.Contains(rect))) Location=location;
        } catch { }
    }
    void SaveSettings() {try {File.WriteAllText(Path.Combine(directory,"settings.json"),new JavaScriptSerializer().Serialize(new {x=Left,y=Top,pinned=TopMost}));}catch{} }
    void LoadCache() {try {usage=new JavaScriptSerializer().Deserialize<Usage>(File.ReadAllText(Path.Combine(directory,"usage-cache.json")));status="CACHED / CONNECTING...";}catch{} }
    void LoadRecoveryControl() {
        try {
            var saved=new JavaScriptSerializer().Deserialize<RecoveryControl>(File.ReadAllText(Path.Combine(directory,"auto-resume.json")));
            if(saved!=null) {
                saved.RunningUntilUtc=saved.RunningUntilUtc.ToUniversalTime();
                saved.BlockedSinceUtc=saved.BlockedSinceUtc.ToUniversalTime();
                saved.RecoveryDetectedUtc=saved.RecoveryDetectedUtc.ToUniversalTime();
                recovery=saved;
            }
        }catch{}
    }
    void UpdateRecoveryLabel() {autoResume.Text=!recovery.Enabled?"AUTO RESUME: OFF":recovery.WaitingForAllowance?"AUTO RESUME: WAITING FOR RESET":"AUTO RESUME: ON";}
    void WriteRecoveryControl() {
        try {
            lastLeaseUtc=DateTime.UtcNow;
            recovery.RunningUntilUtc=quitting?DateTime.UtcNow.AddSeconds(-1):DateTime.UtcNow.AddMinutes(3);
            // ISO timestamps keep the scheduler's control file easy to inspect.
            var json=new JavaScriptSerializer().Serialize(new {Enabled=recovery.Enabled,WaitingForAllowance=recovery.WaitingForAllowance,RunningUntilUtc=recovery.RunningUntilUtc.ToString("O"),BlockedSinceUtc=recovery.BlockedSinceUtc.ToString("O"),RecoveryId=recovery.RecoveryId,RecoveryDetectedUtc=recovery.RecoveryDetectedUtc.ToString("O")});
            // Deserialize DateTime from ISO strings on the next launch; never store credentials here.
            string file=Path.Combine(directory,"auto-resume.json"),temp=file+".tmp";
            File.WriteAllText(temp,json);
            if(File.Exists(file)) File.Replace(temp,file,null);else File.Move(temp,file);
        } catch { }
    }
    async Task RefreshUsage() {
        if(busy || quitting) return;
        busy=true;refresh.Enabled=false;status="SYNCING...";Invalidate();
        try {
            var fresh=await Task.Run(()=>UsageClient.Read());
            if(quitting) return;
            usage=fresh;failed=false;
            RecoveryPolicy.Observe(recovery,fresh,DateTime.UtcNow);UpdateRecoveryLabel();WriteRecoveryControl();
            status="SYNCED "+TimeZoneInfo.ConvertTimeFromUtc(fresh.CheckedUtc,zone).ToString("HH:mm:ss")+" "+ZoneLabel(fresh.CheckedUtc);
            detail="Account-wide allowance. Exact token balance unavailable.";
            try {File.WriteAllText(Path.Combine(directory,"usage-cache.json"),new JavaScriptSerializer().Serialize(fresh));}catch{}
            tray.Text="Codex: 5h "+Percent(fresh.Five)+" / week "+Percent(fresh.Week);
            AccessibleDescription=fresh.Summary();
        } catch(Exception ex) {
            if(quitting) return;
            failed=true;status=usage==null?"OFFLINE / RETRY IN 60s":"OFFLINE / LAST KNOWN USAGE";detail=ex.Message;
        } finally {busy=false;if(!quitting){refresh.Enabled=true;tip.SetToolTip(refresh,detail+" Automatic refresh every 60 seconds.");Invalidate();}}
    }
    string ZoneLabel(DateTime utc) { return zone.IsDaylightSavingTime(TimeZoneInfo.ConvertTimeFromUtc(utc,zone))?"AEDT":"AEST"; }
    static string Percent(WindowUsage w) {return w==null?"--":w.Remaining.ToString("0.#",CultureInfo.InvariantCulture)+"%";}
    static DateTime ResetUtc(WindowUsage w) {return new DateTime(1970,1,1,0,0,0,DateTimeKind.Utc).AddSeconds(w.Reset);}
    bool Expired(WindowUsage w) {return w!=null && ResetUtc(w)<=DateTime.UtcNow;}
    string Countdown(WindowUsage w) {
        if(w==null) return "UNAVAILABLE";
        TimeSpan t=ResetUtc(w)-DateTime.UtcNow;
        if(t.TotalSeconds<=0) return "RESET DUE / SYNC TO CONFIRM";
        return (t.Days>0?t.Days+"d ":"")+t.Hours.ToString("00")+":"+t.Minutes.ToString("00")+":"+t.Seconds.ToString("00")+" TO RESET";
    }
    string ResetLabel(WindowUsage w) {if(w==null) return "No reset time supplied";var dt=ResetUtc(w);return "RESET "+TimeZoneInfo.ConvertTimeFromUtc(dt,zone).ToString("ddd dd MMM HH:mm",CultureInfo.InvariantCulture).ToUpperInvariant()+" "+ZoneLabel(dt);}
    void Label(Graphics g,string value,float size,Color color,float x,float y,float width,bool center) {
        using(var f=new Font("Consolas",size,FontStyle.Regular,GraphicsUnit.Pixel)) using(var b=new SolidBrush(color)) using(var format=new StringFormat {Alignment=center?StringAlignment.Center:StringAlignment.Near,LineAlignment=StringAlignment.Center}) g.DrawString(value,f,b,new RectangleF(x,y,width,Math.Max(24,size*1.6f)),format);
    }
    void GaugeValue(Graphics g,string value) {
        // Measure the glyphs directly: a short layout rectangle can clip the percentage.
        using(var font=new Font("Consolas",43,FontStyle.Regular,GraphicsUnit.Pixel))
        using(var brush=new SolidBrush(amber))
        using(var format=(StringFormat)StringFormat.GenericTypographic.Clone()) {
            format.FormatFlags|=StringFormatFlags.NoClip|StringFormatFlags.NoWrap;
            var bounds=g.MeasureString(value,font,new SizeF(240,90),format);
            g.DrawString(value,font,brush,new PointF(176-bounds.Width/2,170-bounds.Height/2),format);
        }
    }
    Color SegmentColor(int index,int reserveSegments,bool illuminated) {
        if(index<reserveSegments) return illuminated?red:Color.FromArgb(78,43,32);
        return illuminated?amber:track;
    }
    protected override void OnPaint(PaintEventArgs e) {
        base.OnPaint(e);var g=e.Graphics;g.ScaleTransform(scale,scale);g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        using(var pen=new Pen(track)) {g.DrawRectangle(pen,0,0,351,553);g.DrawLine(pen,18,49,334,49);g.DrawLine(pen,18,328,334,328);}
        Label(g,"CODEX // METER",15,amber,18,13,186,false);
        Label(g,"[ FIVE HOUR ]",13,dim,0,58,352,true);
        var five=usage==null?null:usage.Five;var week=usage==null?null:usage.Week;
        bool validFive=five!=null&&!Expired(five),validWeek=week!=null&&!Expired(week);
        int lit=validFive?(int)Math.Ceiling(60*five.Remaining/100):0;
        for(int i=0;i<60;i++) using(var p=new Pen(SegmentColor(i,9,i<lit),9)) g.DrawArc(p,94,91,164,164,-90+i*6,3.8f);
        GaugeValue(g,validFive?Percent(five):"--");
        Label(g,"REMAINING",12,dim,76,190,200,true);
        Label(g,Countdown(five),17,amber,10,271,332,true);
        Label(g,ResetLabel(five),12,dim,8,297,336,true);
        Label(g,"WEEKLY",13,dim,18,337,160,false);
        Label(g,validWeek?Percent(week)+" LEFT":"--",16,amber,197,337,137,true);
        for(int i=0;i<40;i++) using(var b=new SolidBrush(SegmentColor(i,6,validWeek && i<Math.Ceiling(40*week.Remaining/100))))g.FillRectangle(b,19+i*7.9f,370,5.8f,17);
        Label(g,Countdown(week),14,amber,18,393,316,false);
        Label(g,ResetLabel(week),12,dim,18,417,316,false);
        Label(g,status,11,failed?red:dim,18,447,316,false);
    }
}
