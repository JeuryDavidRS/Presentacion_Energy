#region Using directives
using FTOptix.CommunicationDriver;
using FTOptix.Core;
using FTOptix.CoreBase;
using FTOptix.HMIProject;
using FTOptix.Modbus;
using FTOptix.NativeUI;
using FTOptix.NetLogic;
using FTOptix.Recipe;
using FTOptix.Retentivity;
using FTOptix.SQLiteStore;
using FTOptix.Store;
using FTOptix.UI;
using System;
using System.IO;
using System.Text;
using System.Threading;
using UAManagedCore;
using FTOptix.WebUI;
using FTOptix.OPCUAServer;
using OpcUa = UAManagedCore.OpcUa;
#endregion

public class Dashboard_Logic : BaseNetLogic
{
    private PeriodicTask tareaActualizacion;

    private IUAObject parentLine;
    public override void Start()
    {
        parentLine = (IUAObject)LogicObject.Owner;

        var instanceName = parentLine.Owner.Owner.GetAlias("Estacion").BrowseName;

        var rutaHtml = ResourceUri.FromProjectRelativePath($"External_Res/index_{instanceName}.html");
        var rutaData = ResourceUri.FromProjectRelativePath($"External_Res/data_{instanceName}.json"); // ← .json

        string folder = Path.GetDirectoryName(rutaHtml.Uri);
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);

        GenerarHtml(rutaHtml.Uri, $"data_{instanceName}.json");  // ← pasa .json
        ActualizarDatos(rutaData.Uri);

        var browser = (WebBrowser)Owner;
        browser.URL = rutaHtml;
        browser.Refresh();

        tareaActualizacion = new PeriodicTask(Loop, 2500, LogicObject);
        tareaActualizacion.Start();
    }

    public override void Stop()
    {
        // Insert code to be executed when the user-defined logic is stopped
        tareaActualizacion.Dispose();
        tareaActualizacion = null;
    }

    private void Loop()
    {
        var instanceName = parentLine.Owner.Owner.GetAlias("Estacion").BrowseName;
        var rutaData = ResourceUri.FromProjectRelativePath($"External_Res/data_{instanceName}.json");
        ActualizarDatos(rutaData.Uri);
    }

    // ═══════════════════════════════════════════════════════════
    //  ACTUALIZAR DATA.JS
    // ═══════════════════════════════════════════════════════════
    private void ActualizarDatos(string rutaDataJson)
    {
        string Fv(string nombre)
            => ((double)LogicObject.GetVariable(nombre).Value)
               .ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"kV\":{Fv("VoltajeProm")},\"kI\":{Fv("CorrienteTotal")},\"kKW\":{Fv("PotenciaActiva")},");
        sb.AppendLine($"  \"kFP\":{Fv("FactorPotencia")},\"kTHD\":{Fv("THDVoltaje")},\"kFRQ\":{Fv("Frecuencia")},");
        sb.AppendLine($"  \"kDBV\":{Fv("DesbalanceV")},\"kKVA\":{Fv("kVAAparente")},");
        sb.AppendLine($"  \"v1\":{Fv("V1")},\"v2\":{Fv("V2")},\"v3\":{Fv("V3")},");
        sb.AppendLine($"  \"i1\":{Fv("I1")},\"i2\":{Fv("I2")},\"i3\":{Fv("I3")},");
        sb.AppendLine($"  \"w1\":{Fv("W1")},\"w2\":{Fv("W2")},\"w3\":{Fv("W3")},");
        sb.AppendLine($"  \"r1\":{Fv("R1")},\"r2\":{Fv("R2")},\"r3\":{Fv("R3")},");
        sb.AppendLine($"  \"f1\":{Fv("FP1")},\"f2\":{Fv("FP2")},\"f3\":{Fv("FP3")},");
        sb.AppendLine($"  \"thdVL1\":{Fv("THDVL1")},\"thdVL2\":{Fv("THDVL2")},\"thdVL3\":{Fv("THDVL3")},");
        sb.AppendLine($"  \"thdIL1\":{Fv("THDIL1")},\"thdIL2\":{Fv("THDIL2")},\"thdIL3\":{Fv("THDIL3")},");
        sb.AppendLine($"  \"dbI\":{Fv("DesbalanceI")},\"kvar\":{Fv("kVAR")},");
        sb.AppendLine($"  \"kwh\":{Fv("EnergiaKWH")},\"demanda\":{Fv("DemandaMax")},\"co2\":{Fv("EmisionesCO2")},");
        sb.AppendLine($"  \"hL1\":{{\"H1\":{Fv("H1_L1")},\"H3\":{Fv("H3_L1")},\"H5\":{Fv("H5_L1")},\"H7\":{Fv("H7_L1")},\"H9\":{Fv("H9_L1")}}},");
        sb.AppendLine($"  \"hL2\":{{\"H1\":{Fv("H1_L2")},\"H3\":{Fv("H3_L2")},\"H5\":{Fv("H5_L2")},\"H7\":{Fv("H7_L2")},\"H9\":{Fv("H9_L2")}}},");
        sb.AppendLine($"  \"hL3\":{{\"H1\":{Fv("H1_L3")},\"H3\":{Fv("H3_L3")},\"H5\":{Fv("H5_L3")},\"H7\":{Fv("H7_L3")},\"H9\":{Fv("H9_L3")}}}");
        sb.AppendLine("}");

        File.WriteAllText(rutaDataJson, sb.ToString());
    }

    // ═══════════════════════════════════════════════════════════
    //  GENERAR HTML — llamado UNA sola vez
    // ═══════════════════════════════════════════════════════════
    private static void GenerarHtml(string rutaHtml, string dataJsNombre)
    {
        var h = new StringBuilder();

        h.AppendLine("<!DOCTYPE html>");
        h.AppendLine("<html lang='es'><head>");
        h.AppendLine("<meta charset='UTF-8'>");
        h.AppendLine("<meta name='viewport' content='width=device-width, initial-scale=1.0'>");
        h.AppendLine("<title>Smart Energy Monitor</title>");

        // ── CSS ────────────────────────────────────────────────
        h.AppendLine("<style>");
        h.AppendLine(":root{--bg:#040810;--card:#070e1c;--brd:rgba(0,210,255,0.10);--brdhi:rgba(0,210,255,0.35);--cyan:#00d2ff;--cdim:rgba(0,210,255,0.12);--green:#00ff9d;--gdim:rgba(0,255,157,0.10);--amber:#ffb300;--adim:rgba(255,179,0,0.12);--red:#ff3d5a;--rdim:rgba(255,61,90,0.12);--txt:#e8f4ff;--txt2:rgba(200,220,255,0.50);--fd:'Exo 2',sans-serif;--fl:'Rajdhani',sans-serif;--fm:'Share Tech Mono',monospace;--h1c:#00d2ff;--h3c:#ffd93d;--h5c:#ff6b6b;--h7c:#a8ff78;--h9c:#c77dff;}");
        h.AppendLine("*,*::before,*::after{margin:0;padding:0;box-sizing:border-box;}");
        h.AppendLine("html,body{width:100%;height:100%;background:var(--bg);color:var(--txt);font-family:var(--fl);overflow:hidden;}");
        h.AppendLine("body::before{content:'';position:fixed;inset:0;background-image:linear-gradient(rgba(0,210,255,.022) 1px,transparent 1px),linear-gradient(90deg,rgba(0,210,255,.022) 1px,transparent 1px);background-size:40px 40px;pointer-events:none;z-index:0;}");
        h.AppendLine(".shell{position:relative;z-index:1;width:100%;height:100%;display:grid;grid-template-rows:clamp(65px,9vh,90px) clamp(56px,7.5vh,70px) 1fr clamp(120px,14vh,160px);zoom:1.2;}");
        // Header
        h.AppendLine("header{display:flex;align-items:center;padding:0 22px;gap:12px;border-bottom:1px solid var(--brd);background:linear-gradient(180deg,rgba(0,28,66,.5) 0%,transparent 100%);}");
        h.AppendLine(".lbox{width:42px;height:42px;border:1.5px solid var(--cyan);border-radius:7px;display:flex;align-items:center;justify-content:center;box-shadow:0 0 14px rgba(0,210,255,.45);animation:pb 2.8s ease-in-out infinite;flex-shrink:0;}");
        h.AppendLine("@keyframes pb{0%,100%{box-shadow:0 0 10px rgba(0,210,255,.4);}50%{box-shadow:0 0 22px rgba(0,210,255,.8);}}");
        h.AppendLine(".lbox svg{width:22px;height:22px;}");
        h.AppendLine(".lt h1{font-family:var(--fd);font-size:clamp(14px,2vh,20px);font-weight:800;letter-spacing:2.5px;text-transform:uppercase;color:var(--cyan);line-height:1;}");
        h.AppendLine(".lt p{font-size:clamp(8px,1.2vh,11px);letter-spacing:1.5px;text-transform:uppercase;color:var(--txt2);margin-top:4px;}");
        h.AppendLine(".hlogos{display:flex;align-items:center;gap:14px;margin-left:auto;padding-right:4px;}");
        h.AppendLine(".hlogos img{height:clamp(38px,5vh,52px);width:auto;object-fit:contain;opacity:0.90;filter:brightness(1.1);}");
        // KPI bar — sin .dt badge
        h.AppendLine(".kbar{display:grid;grid-template-columns:repeat(8,1fr);border-bottom:1px solid var(--brd);padding:0 12px;overflow-x:auto;min-height:0;}");
        h.AppendLine(".kpi{display:flex;flex-direction:column;justify-content:center;padding:0 16px;border-right:1px solid var(--brd);transition:background .2s;position:relative;}");
        h.AppendLine(".kpi:last-child{border-right:none;}");
        h.AppendLine(".kpi:hover{background:var(--cdim);}");
        h.AppendLine(".kpi::after{content:'';position:absolute;bottom:0;left:0;width:0;height:2px;background:var(--cyan);transition:width .45s;}");
        h.AppendLine(".kpi:hover::after{width:100%;}");
        h.AppendLine(".kl{font-size:11.5px;letter-spacing:1.8px;text-transform:uppercase;color:var(--txt2);font-family:var(--fl);font-weight:600;margin-bottom:3px;}");
        h.AppendLine(".kv{font-family:var(--fm);font-size:clamp(20px,2.8vh,28px);line-height:1;color:var(--txt);transition:color .3s;}");
        h.AppendLine(".kv.fl{color:var(--cyan);text-shadow:0 0 10px rgba(0,210,255,.7);}");
        h.AppendLine(".ku{font-size:11px;color:var(--txt2);margin-top:3px;letter-spacing:.6px;}");
        // Main
        h.AppendLine(".main{display:grid;grid-template-columns:1.05fr 0.95fr 1.2fr;gap:clamp(6px,1vw,16px);padding:clamp(4px,1vh,10px) clamp(6px,1vw,16px) 4px;overflow:hidden;min-height:0;}");
        h.AppendLine(".card{background:var(--card);border:1px solid var(--brd);border-radius:10px;display:flex;flex-direction:column;overflow:hidden;position:relative;}");
        h.AppendLine(".card::before{content:'';position:absolute;top:0;left:0;right:0;height:1px;background:linear-gradient(90deg,transparent,var(--cyan),transparent);opacity:.35;}");
        h.AppendLine(".ch{display:flex;align-items:center;justify-content:space-between;padding:10px 16px;border-bottom:1px solid var(--brd);flex-shrink:0;}");
        h.AppendLine(".ct{font-family:var(--fd);font-size:11.5px;font-weight:700;letter-spacing:2.5px;text-transform:uppercase;color:var(--cyan);}");
        h.AppendLine(".badge{font-family:var(--fm);font-size:10px;padding:3px 8px;border-radius:10px;letter-spacing:.7px;}");
        h.AppendLine(".bg{color:var(--green);background:var(--gdim);border:1px solid rgba(0,255,157,.2);}");
        h.AppendLine(".ba{color:var(--amber);background:var(--adim);border:1px solid rgba(255,179,0,.2);}");
        h.AppendLine(".bc{color:var(--cyan);background:var(--cdim);border:1px solid rgba(0,210,255,.2);}");
        // Phase table
        h.AppendLine(".ptbl{width:100%;border-collapse:collapse;}");
        h.AppendLine(".ptbl th{padding:8px 10px;font-size:10.5px;letter-spacing:1.8px;text-transform:uppercase;color:var(--txt2);text-align:right;border-bottom:1px solid var(--brd);font-family:var(--fl);font-weight:600;}");
        h.AppendLine(".ptbl th:first-child{text-align:left;}");
        h.AppendLine(".ptbl td{padding:10px 10px;font-family:var(--fm);font-size:15px;text-align:right;border-bottom:1px solid rgba(0,210,255,.04);}");
        h.AppendLine(".ptbl td:first-child{text-align:left;font-family:var(--fl);font-weight:700;font-size:15px;letter-spacing:1.5px;}");
        h.AppendLine(".rl1 td:first-child{color:#ff6b6b;}.rl2 td:first-child{color:#ffd93d;}.rl3 td:first-child{color:#6bcbff;}");
        h.AppendLine(".rtot td:first-child{color:var(--green);font-size:10px;letter-spacing:2px;}");
        h.AppendLine(".ptbl tr:hover td{background:rgba(0,210,255,.022);}");
        // Sparklines
        h.AppendLine(".sparks{flex:1;display:flex;flex-direction:column;justify-content:space-around;padding:14px 14px;gap:10px;overflow:hidden;}");
        h.AppendLine(".srow{display:flex;align-items:center;gap:10px;padding:6px 0;border-bottom:1px solid rgba(0,210,255,.04);}");
        h.AppendLine(".srow:last-child{border-bottom:none;}");
        h.AppendLine(".slbl{width:24px;font-size:13px;font-family:var(--fl);font-weight:700;flex-shrink:0;}");
        h.AppendLine(".ssvg{flex:1;height:28px;}");
        h.AppendLine(".sval{width:62px;font-family:var(--fm);font-size:13px;text-align:right;color:var(--txt);flex-shrink:0;}");
        // THD bars
        h.AppendLine(".thdbody{flex:1;padding:10px 14px;display:flex;flex-direction:column;justify-content:space-around;overflow:hidden;}");
        h.AppendLine(".trow{display:flex;align-items:center;gap:8px;}");
        h.AppendLine(".tlbl{width:82px;font-size:11px;letter-spacing:.7px;color:var(--txt2);font-family:var(--fl);font-weight:600;}");
        h.AppendLine(".tbg{flex:1;height:5px;background:rgba(255,255,255,.06);border-radius:3px;overflow:hidden;}");
        h.AppendLine(".tfill{height:100%;border-radius:3px;transition:width 1.2s cubic-bezier(.16,1,.3,1);}");
        h.AppendLine(".fc{background:linear-gradient(90deg,var(--cyan),rgba(0,210,255,.4));}.fa{background:linear-gradient(90deg,var(--amber),rgba(255,179,0,.4));}");
        h.AppendLine(".tval{width:38px;text-align:right;font-family:var(--fm);font-size:12px;flex-shrink:0;}");
        h.AppendLine(".tdiv{height:1px;background:var(--brd);margin:4px 0;}");
        h.AppendLine(".tsub{font-size:9px;letter-spacing:1.8px;text-transform:uppercase;color:var(--txt2);font-family:var(--fl);margin-bottom:2px;}");
        h.AppendLine(".extra{display:flex;border-top:1px solid var(--brd);flex-shrink:0;}");
        h.AppendLine(".ecell{flex:1;padding:8px 10px;border-right:1px solid var(--brd);}.ecell:last-child{border-right:none;}");
        h.AppendLine(".el{font-size:9px;letter-spacing:1.5px;text-transform:uppercase;color:var(--txt2);font-family:var(--fl);margin-bottom:3px;}");
        h.AppendLine(".ev{font-family:var(--fm);font-size:15px;}");
        // Colores semáforo
        h.AppendLine(".ok{color:var(--green)!important;text-shadow:0 0 8px rgba(0,255,157,.5);}");
        h.AppendLine(".warn{color:var(--amber)!important;text-shadow:0 0 8px rgba(255,179,0,.5);}");
        h.AppendLine(".bad{color:var(--red)!important;text-shadow:0 0 8px rgba(255,61,90,.5);}");
        // Card potencia
        h.AppendLine(".pwbody{flex:1;display:flex;flex-direction:column;padding:0;overflow:hidden;}");
        h.AppendLine("#pwChart{flex:1;min-height:0;width:100%;}");
        h.AppendLine(".pwextra{display:flex;border-top:1px solid var(--brd);flex-shrink:0;}");
        // Harmonic tabs
        h.AppendLine(".htabs{display:flex;border-bottom:1px solid var(--brd);flex-shrink:0;}");
        h.AppendLine(".htab{flex:1;padding:6px 0;text-align:center;font-family:var(--fm);font-size:11px;cursor:pointer;border-right:1px solid var(--brd);color:var(--txt2);transition:all .2s;letter-spacing:1px;position:relative;}");
        h.AppendLine(".htab:last-child{border-right:none;}");
        h.AppendLine(".htab.on{color:var(--cyan);background:var(--cdim);}");
        h.AppendLine(".htab.on::after{content:'';position:absolute;bottom:0;left:0;right:0;height:2px;background:var(--cyan);}");
        h.AppendLine(".htab span{display:block;font-size:8.5px;opacity:.6;letter-spacing:1.5px;margin-top:1px;}");
        h.AppendLine(".hleg{display:flex;gap:12px;padding:6px 14px;border-bottom:1px solid var(--brd);flex-shrink:0;}");
        h.AppendLine(".hli{display:flex;align-items:center;gap:5px;font-family:var(--fm);font-size:9.5px;color:var(--txt2);letter-spacing:.4px;}");
        h.AppendLine(".hld{width:18px;height:2px;border-radius:2px;}");
        // ECharts container — ocupa todo el espacio restante de la card
        h.AppendLine("#hChart{flex:1;min-height:0;}");
        // Bottom cards
        h.AppendLine(".bottom{display:grid;grid-template-columns:1.05fr 0.95fr 1.2fr;gap:clamp(6px,1vw,16px);padding:0 clamp(6px,1vw,16px) clamp(4px,1vh,10px);min-height:0;align-items:stretch;}");
        h.AppendLine(".ec{background:var(--card);border:1px solid var(--brd);border-radius:10px;padding:clamp(6px,1vh,12px) clamp(8px,1vw,15px);position:relative;overflow:hidden;display:flex;flex-direction:column;justify-content:space-between;transition:border-color .3s,transform .3s;}");
        h.AppendLine(".ec:hover{border-color:var(--brdhi);transform:translateY(-2px);}");
        h.AppendLine(".ec::after{content:'';position:absolute;bottom:-22px;right:-22px;width:70px;height:70px;border-radius:50%;opacity:.07;}");
        h.AppendLine(".ec1::after{background:var(--cyan);}.ec2::after{background:var(--green);}.ec3::after{background:var(--amber);}.ec4::after{background:#a8ff78;}");
        h.AppendLine(".etop{display:flex;align-items:center;justify-content:space-between;}");
        h.AppendLine(".eico{font-size:15px;}");
        h.AppendLine(".etag{font-family:var(--fm);font-size:9px;padding:2px 6px;border-radius:4px;color:var(--txt2);background:rgba(255,255,255,.05);letter-spacing:.4px;}");
        h.AppendLine(".elbl{font-size:9px;letter-spacing:1.5px;text-transform:uppercase;color:var(--txt2);font-family:var(--fl);margin-top:6px;}");
        h.AppendLine(".eval{font-family:var(--fm);font-size:clamp(14px,2.2vh,22px);line-height:1;margin-top:2px;}");
        h.AppendLine(".ec1 .eval{color:var(--cyan);}.ec2 .eval{color:var(--green);}.ec3 .eval{color:var(--amber);}.ec4 .eval{color:#a8ff78;}");
        h.AppendLine(".eunit{font-size:9px;color:var(--txt2);margin-top:2px;}");
        h.AppendLine(".esub{display:flex;align-items:center;gap:4px;font-family:var(--fm);font-size:9.5px;color:var(--txt2);margin-top:6px;border-top:1px solid var(--brd);padding-top:5px;}");
        h.AppendLine(".dbw{margin-top:5px;}");
        h.AppendLine(".dbm{display:flex;justify-content:space-between;font-family:var(--fm);font-size:9px;color:var(--txt2);margin-bottom:4px;}");
        h.AppendLine(".dbb{height:5px;background:rgba(255,255,255,.06);border-radius:3px;overflow:hidden;}");
        h.AppendLine(".dbf{height:100%;border-radius:3px;background:linear-gradient(90deg,var(--amber),var(--red));box-shadow:0 0 7px rgba(255,61,90,.3);transition:width 1.2s;}");
        h.AppendLine("</style></head><body>");
        h.AppendLine("<div class='shell'>");

        // ── HEADER ────────────────────────────────────────────
        h.AppendLine("<header>");
        h.AppendLine("  <div class='lbox'><svg viewBox='0 0 24 24' fill='none' stroke='#00d2ff' stroke-width='2.2' stroke-linecap='round'><polygon points='13 2 3 14 12 14 11 22 21 10 12 10 13 2'/></svg></div>");
        h.AppendLine("  <div class='lt'><h1>Smart Energy Monitor</h1><p>Analizador de Red Trifásica</p></div>");
        h.AppendLine("  <div class='hlogos'>");
        h.AppendLine("    <img src='./logo1.png' alt='Logo 1'>");
        h.AppendLine("    <img src='./logo2.png' alt='Logo 2'>");
        h.AppendLine("  </div>");
        h.AppendLine("</header>");

        // ── KPI BAR ───────────────────────────────────────────
        h.AppendLine("<div class='kbar'>");
        KPI(h, "kV", "Voltaje Prom.", "V (L-L)");
        KPI(h, "kI", "Corriente Total", "A");
        KPI(h, "kKW", "Potencia Activa", "kW");
        KPI(h, "kFP", "Factor Potencia", "cos φ");
        KPI(h, "kTHD", " Max. THD Voltaje", "%");
        KPI(h, "kFRQ", "Frecuencia", "Hz");
        KPI(h, "kDBV", "Desbalance V", "%");
        KPI(h, "kKVA", "kVA Aparente", "kVA");
        h.AppendLine("</div>");

        // ── MAIN ──────────────────────────────────────────────
        h.AppendLine("<div class='main'>");

        // Card 1: Tabla de fases + sparklines
        h.AppendLine("  <div class='card'>");
        h.AppendLine("    <div class='ch'><span class='ct'>⚡ Estado por Fase</span></div>");
        h.AppendLine("    <table class='ptbl'><thead><tr><th>FASE</th><th>V (V)</th><th>I (A)</th><th>kW</th><th>kVAR</th><th>FP</th></tr></thead><tbody>");
        h.AppendLine("      <tr class='rl1'><td>L1</td><td id='v1'>--</td><td id='i1'>--</td><td id='w1'>--</td><td id='r1'>--</td><td id='f1'>--</td></tr>");
        h.AppendLine("      <tr class='rl2'><td>L2</td><td id='v2'>--</td><td id='i2'>--</td><td id='w2'>--</td><td id='r2'>--</td><td id='f2'>--</td></tr>");
        h.AppendLine("      <tr class='rl3'><td>L3</td><td id='v3'>--</td><td id='i3'>--</td><td id='w3'>--</td><td id='r3'>--</td><td id='f3'>--</td></tr>");
        h.AppendLine("      <tr class='rtot'><td>TOTAL</td><td id='vT'>--</td><td id='iT'>--</td><td id='wT'>--</td><td id='rT'>--</td><td id='fT'>--</td></tr>");
        h.AppendLine("    </tbody></table>");
        h.AppendLine("    <div class='sparks'>");
        h.AppendLine("      <div style='font-size:9px;letter-spacing:2px;color:var(--txt2);margin-bottom:4px;font-family:var(--fl);text-transform:uppercase;'>Tendencia Voltaje — Últimas lecturas</div>");
        h.AppendLine("      <div class='srow'><span class='slbl' style='color:#ff6b6b'>L1</span><svg class='ssvg' id='sp1' viewBox='0 0 200 20' preserveAspectRatio='none'></svg><span class='sval' id='sv1'>-- V</span></div>");
        h.AppendLine("      <div class='srow'><span class='slbl' style='color:#ffd93d'>L2</span><svg class='ssvg' id='sp2' viewBox='0 0 200 20' preserveAspectRatio='none'></svg><span class='sval' id='sv2'>-- V</span></div>");
        h.AppendLine("      <div class='srow'><span class='slbl' style='color:#6bcbff'>L3</span><svg class='ssvg' id='sp3' viewBox='0 0 200 20' preserveAspectRatio='none'></svg><span class='sval' id='sv3'>-- V</span></div>");
        h.AppendLine("    </div>");
        h.AppendLine("  </div>");

        // Card 2: THD Quality
        h.AppendLine("  <div class='card'>");
        h.AppendLine("    <div class='ch'><span class='ct'>📊 Calidad Eléctrica — THD</span></div>");
        h.AppendLine("    <div class='thdbody'>");
        h.AppendLine("      <div class='tsub'>Distorsión Armónica — Voltaje</div>");
        h.AppendLine("      <div class='trow'><span class='tlbl' style='color:#ff6b6b'>THD V · L1</span><div class='tbg'><div class='tfill fc' id='tvl1' style='width:0%'></div></div><span class='tval' style='color:#ff6b6b' id='tvv1'>--%</span></div>");
        h.AppendLine("      <div class='trow'><span class='tlbl' style='color:#ffd93d'>THD V · L2</span><div class='tbg'><div class='tfill fc' id='tvl2' style='width:0%'></div></div><span class='tval' style='color:#ffd93d' id='tvv2'>--%</span></div>");
        h.AppendLine("      <div class='trow'><span class='tlbl' style='color:#6bcbff'>THD V · L3</span><div class='tbg'><div class='tfill fc' id='tvl3' style='width:0%'></div></div><span class='tval' style='color:#6bcbff' id='tvv3'>--%</span></div>");
        h.AppendLine("      <div class='tdiv'></div>");
        h.AppendLine("      <div class='tsub'>Distorsión Armónica — Corriente</div>");
        h.AppendLine("      <div class='trow'><span class='tlbl' style='color:#ff6b6b'>THD I · L1</span><div class='tbg'><div class='tfill fa' id='til1' style='width:0%'></div></div><span class='tval' style='color:var(--amber)' id='tiv1'>--%</span></div>");
        h.AppendLine("      <div class='trow'><span class='tlbl' style='color:#ffd93d'>THD I · L2</span><div class='tbg'><div class='tfill fa' id='til2' style='width:0%'></div></div><span class='tval' style='color:var(--amber)' id='tiv2'>--%</span></div>");
        h.AppendLine("      <div class='trow'><span class='tlbl' style='color:#6bcbff'>THD I · L3</span><div class='tbg'><div class='tfill fa' id='til3' style='width:0%'></div></div><span class='tval' style='color:var(--amber)' id='tiv3'>--%</span></div>");
        h.AppendLine("    </div>");
        h.AppendLine("    <div class='extra'>");
        h.AppendLine("      <div class='ecell'><div class='el'>Desbalance V</div><div class='ev' style='color:var(--cyan)' id='dbV'>--%</div></div>");
        h.AppendLine("      <div class='ecell'><div class='el'>Desbalance I</div><div class='ev' style='color:var(--amber)' id='dbI'>--%</div></div>");
        h.AppendLine("      <div class='ecell'><div class='el'>Frec.</div><div class='ev' style='color:var(--green)' id='frqB'>-- Hz</div></div>");
        h.AppendLine("      <div class='ecell'><div class='el'>kVAR</div><div class='ev' id='kvarB'>--</div></div>");
        h.AppendLine("    </div>");
        h.AppendLine("  </div>");

        // Card 3: Potencia Activa por Fase
        h.AppendLine("  <div class='card'>");
        h.AppendLine("    <div class='ch'><span class='ct'>⚡ Tendencia Potencia Activa</span><span class='badge bc' id='pwBadge'>-- kW</span></div>");
        h.AppendLine("    <div class='pwbody'>");
        h.AppendLine("      <div id='pwChart'></div>");
        h.AppendLine("    </div>");
        h.AppendLine("  </div>");

        h.AppendLine("</div>"); // /main

        // ── BOTTOM CARDS ──────────────────────────────────────
        h.AppendLine("<div class='bottom'>");
        // REEMPLAZA la línea de ec1:
        h.AppendLine("  <div class='ec ec1'><div><div class='etop'><span class='eico'>⚡</span><span class='etag'>HOY</span></div><div class='elbl'>Energía Activa Actual</div><div class='eval' id='ecKWH'>--</div><div class='eunit'>kWh consumidos hoy</div></div><div class='esub' id='esubKWH'><span id='kwhIco' style='color:var(--green)'>▲</span><span id='kwhTxt'>Cargando...</span></div></div>");
        //h.AppendLine("  <div class='ec ec2'><div><div class='etop'><span class='eico'>💰</span><span class='etag'>HOY</span></div><div class='elbl'>Costo Energético del Día</div><div class='eval' id='ecCOST'>--</div><div class='eunit'>Pesos estimados hoy</div></div><div class='esub'><span style='color:var(--txt2)'>●</span>Tarifa: $3.19 / kWh</div></div>");
        // REEMPLAZA la línea de ec3 completa:
        h.AppendLine("  <div class='ec ec2'><div><div class='etop'><span class='eico'>📈</span><span class='etag'>AHORA</span></div><div class='elbl'>Demanda Activa</div><div class='eval' id='ecDEM'>--</div><div class='eunit'>kW en este momento</div></div><div class='esub'><span style='color:var(--txt2)'>●</span>Medición instantánea</div></div>");
        // REEMPLAZA la línea de ec4:
        h.AppendLine("  <div class='ec ec3'><div><div class='etop'><span class='eico'>🌱</span><span class='etag'>AHORA</span></div><div class='elbl'>Emisiones CO₂ Instantáneas</div><div class='eval' id='ecCO2'>--</div><div class='eunit'>kg CO₂eq / kWh</div></div><div class='esub'><span style='color:#a8ff78'>●</span>Medición instantánea</div></div>");
        h.AppendLine("</div>"); // /bottom

        h.AppendLine("</div>"); // /shell

        var instanceName = dataJsNombre.Replace(".js", ""); // Extrae el nombre base sin extensión
        instanceName = instanceName.Replace("data_", ""); // Reemplaza guiones por guiones bajos para un nombre de variable válido

        // ── SCRIPTS ───────────────────────────────────────────
        // echarts.min.js local — en la misma carpeta que index.html
        h.AppendLine("<script src='./echarts.min.js'><" + "/script>");
        h.AppendLine($"<script src='./app_{instanceName}.js'><" + "/script>");
        h.AppendLine("</body></html>");
        File.WriteAllText(rutaHtml, h.ToString());

        // ✅ Genera app.js en la misma carpeta
        string rutaAppJs = Path.Combine(Path.GetDirectoryName(rutaHtml), $"app_{instanceName}.js");
        GenerarAppJs(rutaAppJs, dataJsNombre);
    }
    private static void GenerarAppJs(string rutaAppJs, string dataJsNombre)
    {
        var js = new StringBuilder();

        // ── Buffers ────────────────────────────────────────────
        js.AppendLine("var PW_N=50;");
        js.AppendLine("var pwBuf={");
        js.AppendLine("  l1:Array(PW_N).fill(null),");
        js.AppendLine("  l2:Array(PW_N).fill(null),");
        js.AppendLine("  l3:Array(PW_N).fill(null),");
        js.AppendLine("  tot:Array(PW_N).fill(null)");
        js.AppendLine("};");
        js.AppendLine("var pwBufInit=false;");
        js.AppendLine("var pwChart=null;");

        js.AppendLine("var spBuf=[");
        js.AppendLine("  Array.from({length:22},function(){return +(219.8+(Math.random()-.5)*2.4).toFixed(1);}),");
        js.AppendLine("  Array.from({length:22},function(){return +(221.3+(Math.random()-.5)*2.4).toFixed(1);}),");
        js.AppendLine("  Array.from({length:22},function(){return +(220.1+(Math.random()-.5)*2.4).toFixed(1);})");
        js.AppendLine("];");

        // ── Sparklines ─────────────────────────────────────────
        js.AppendLine("function drawSpark(id,data,col){");
        js.AppendLine("  var svg=document.getElementById(id);if(!svg)return;");
        js.AppendLine("  var W=200,H=20,mn=Math.min.apply(null,data),mx=Math.max.apply(null,data);");
        js.AppendLine("  var pts=data.map(function(v,i){return (i/(data.length-1))*W+','+(H-((v-mn)/(mx-mn||1))*(H-4)-2);}).join(' ');");
        js.AppendLine("  svg.innerHTML='<polyline points=\"'+pts+'\" fill=\"none\" stroke=\"'+col+'\" stroke-width=\"1.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\" opacity=\"0.85\"/><polyline points=\"0,20 '+pts+' 200,20\" fill=\"'+col+'\" fill-opacity=\"0.07\" stroke=\"none\"/>';");
        js.AppendLine("}");
        js.AppendLine("function updateSparks(){");
        js.AppendLine("  drawSpark('sp1',spBuf[0],'#ff6b6b');");
        js.AppendLine("  drawSpark('sp2',spBuf[1],'#ffd93d');");
        js.AppendLine("  drawSpark('sp3',spBuf[2],'#6bcbff');");
        js.AppendLine("}");

        // ── Semáforo ───────────────────────────────────────────
        js.AppendLine("function sem(id,cls){");
        js.AppendLine("  var el=document.getElementById(id);if(!el)return;");
        js.AppendLine("  el.classList.remove('ok','warn','bad');");
        js.AppendLine("  el.classList.add(cls);");
        js.AppendLine("}");

        js.AppendLine("function colorear(D){");
        js.AppendLine("  var v=+D.kV;");
        js.AppendLine("  sem('kV',v>=209&&v<=231?'ok':v>=198&&v<=242?'warn':'bad');");
        js.AppendLine("  var thd=+D.kTHD;");
        js.AppendLine("  sem('kTHD',thd<3?'ok':thd<=5?'warn':'bad');");
        js.AppendLine("  var dbv=+D.kDBV;");
        js.AppendLine("  sem('kDBV',dbv<1?'ok':dbv<=2?'warn':'bad');");
        js.AppendLine("  var fp=+D.kFP;");
        js.AppendLine("  sem('kFP',fp>=0.95?'ok':fp>=0.90?'warn':'bad');");
        js.AppendLine("  ['1','2','3'].forEach(function(l){");
        js.AppendLine("    var fpl=+D['f'+l];");
        js.AppendLine("    sem('f'+l,fpl>=0.95?'ok':fpl>=0.90?'warn':'bad');");
        js.AppendLine("  });");
        js.AppendLine("  var dbi=+D.dbI;");
        js.AppendLine("  sem('dbI',dbi<1?'ok':dbi<=2?'warn':'bad');");
        js.AppendLine("}");

        // ── initPwChart ────────────────────────────────────────
        js.AppendLine("function initPwChart(){");
        js.AppendLine("  if(typeof echarts==='undefined'){setTimeout(initPwChart,100);return;}");
        js.AppendLine("  pwChart=echarts.init(document.getElementById('pwChart'),null,{renderer:'svg'});");
        js.AppendLine("  pwChart.setOption({");
        js.AppendLine("    backgroundColor:'transparent',");
        js.AppendLine("    color:['#ff6b6b','#ffd93d','#6bcbff','#00ff9d'],");
        js.AppendLine("    animation:false,");
        js.AppendLine("    grid:{top:18,bottom:24,left:10,right:70,containLabel:false},");
        js.AppendLine("    legend:{");
        js.AppendLine("      top:2,right:62,");
        js.AppendLine("      textStyle:{color:'rgba(200,220,255,0.55)',fontFamily:'monospace',fontSize:9},");
        js.AppendLine("      itemWidth:14,itemHeight:2,");
        js.AppendLine("      data:[");
        js.AppendLine("        {name:'L1',icon:'rect'},");
        js.AppendLine("        {name:'L2',icon:'rect'},");
        js.AppendLine("        {name:'L3',icon:'rect'},");
        js.AppendLine("        {name:'Total',icon:'rect'}");
        js.AppendLine("      ]");
        js.AppendLine("    },");
        js.AppendLine("    tooltip:{");
        js.AppendLine("      trigger:'axis',");
        js.AppendLine("      backgroundColor:'rgba(4,8,16,0.92)',");
        js.AppendLine("      borderColor:'rgba(0,210,255,0.25)',");
        js.AppendLine("      textStyle:{color:'#e8f4ff',fontFamily:'monospace',fontSize:10},");
        js.AppendLine("      formatter:function(p){");
        js.AppendLine("        return p.filter(function(s){return s.value!==null;})");
        js.AppendLine("               .map(function(s){");
        js.AppendLine("                 return '<span style=\"color:'+s.color+'\">●</span> '+s.seriesName+': <b>'+s.value.toFixed(2)+'</b> kW';");
        js.AppendLine("               }).join('<br>');");
        js.AppendLine("      }");
        js.AppendLine("    },");
        js.AppendLine("    xAxis:{");
        js.AppendLine("      type:'category',");
        js.AppendLine("      boundaryGap:false,");
        js.AppendLine("      data:Array.from({length:PW_N},function(_,i){return i;}),");
        js.AppendLine("      axisLine:{lineStyle:{color:'rgba(0,210,255,0.08)'}},");
        js.AppendLine("      axisTick:{show:false},");
        js.AppendLine("      axisLabel:{");
        js.AppendLine("        color:'rgba(200,220,255,0.3)',fontFamily:'monospace',fontSize:7,");
        js.AppendLine("        formatter:function(v,i){");
        js.AppendLine("          var s=Math.round((PW_N-1-i)*2.5);");
        js.AppendLine("          return i===PW_N-1?'NOW':(i===0||i===Math.floor(PW_N/2)?'-'+s+'s':'');");
        js.AppendLine("        }");
        js.AppendLine("      }");
        js.AppendLine("    },");
        js.AppendLine("    yAxis:[");
        js.AppendLine("      {");  // eje izquierdo — fases individuales
        js.AppendLine("        type:'value',scale:true,");
        js.AppendLine("        splitLine:{lineStyle:{color:'rgba(0,210,255,0.06)'}},");
        js.AppendLine("        axisLabel:{color:'rgba(200,220,255,0.3)',fontFamily:'monospace',fontSize:7,");
        js.AppendLine("          formatter:function(v){return v+' kW';}");
        js.AppendLine("        },");
        js.AppendLine("        axisLine:{show:false},axisTick:{show:false}");
        js.AppendLine("      },");
        js.AppendLine("      {");  // eje derecho — Total
        js.AppendLine("        type:'value',scale:true,");
        js.AppendLine("        splitLine:{show:false},");
        js.AppendLine("        axisLabel:{color:'rgba(0,255,157,0.4)',fontFamily:'monospace',fontSize:7,");
        js.AppendLine("          formatter:function(v){return v+' kW';}");
        js.AppendLine("        },");
        js.AppendLine("        axisLine:{show:false},axisTick:{show:false}");
        js.AppendLine("      }");
        js.AppendLine("    ],");
        js.AppendLine("    series:[");
        // L1 — agrega yAxisIndex:0
        js.AppendLine("      {name:'L1',type:'line',yAxisIndex:0,data:[],smooth:true,symbol:'none',");
        js.AppendLine("       lineStyle:{color:'#ff6b6b',width:1.5},");
        js.AppendLine("       areaStyle:{color:{type:'linear',x:0,y:0,x2:0,y2:1,colorStops:[{offset:0,color:'#ff6b6b28'},{offset:1,color:'#ff6b6b00'}]}},");
        js.AppendLine("       endLabel:{show:true,formatter:function(p){return p.value.toFixed(1);},color:'#ff6b6b',fontFamily:'monospace',fontSize:8}},");

        // L2 — yAxisIndex:0
        js.AppendLine("      {name:'L2',type:'line',yAxisIndex:0,data:[],smooth:true,symbol:'none',");
        js.AppendLine("       lineStyle:{color:'#ffd93d',width:1.5},");
        js.AppendLine("       areaStyle:{color:{type:'linear',x:0,y:0,x2:0,y2:1,colorStops:[{offset:0,color:'#ffd93d28'},{offset:1,color:'#ffd93d00'}]}},");
        js.AppendLine("       endLabel:{show:true,formatter:function(p){return p.value.toFixed(1);},color:'#ffd93d',fontFamily:'monospace',fontSize:8}},");

        // L3 — yAxisIndex:0
        js.AppendLine("      {name:'L3',type:'line',yAxisIndex:0,data:[],smooth:true,symbol:'none',");
        js.AppendLine("       lineStyle:{color:'#6bcbff',width:1.5},");
        js.AppendLine("       areaStyle:{color:{type:'linear',x:0,y:0,x2:0,y2:1,colorStops:[{offset:0,color:'#6bcbff28'},{offset:1,color:'#6bcbff00'}]}},");
        js.AppendLine("       endLabel:{show:true,formatter:function(p){return p.value.toFixed(1);},color:'#6bcbff',fontFamily:'monospace',fontSize:8}},");

        // Total — yAxisIndex:1 (eje derecho, escala propia)
        js.AppendLine("      {name:'Total',type:'line',yAxisIndex:1,data:[],smooth:true,symbol:'none',");
        js.AppendLine("       lineStyle:{color:'#00ff9d',width:2.5,type:[6,3]},");
        js.AppendLine("       endLabel:{show:true,formatter:function(p){return p.value.toFixed(1)+' kW';},color:'#00ff9d',fontFamily:'monospace',fontSize:9,fontWeight:'bold'}}");
        js.AppendLine("    ]");
        js.AppendLine("  });");
        js.AppendLine("}");

        // ── refreshPwChart ─────────────────────────────────────
        js.AppendLine("function refreshPwChart(){");
        js.AppendLine("  if(!pwChart)return;");
        js.AppendLine("  pwChart.setOption({");
        js.AppendLine("    series:[");
        js.AppendLine("      {data:pwBuf.l1},");
        js.AppendLine("      {data:pwBuf.l2},");
        js.AppendLine("      {data:pwBuf.l3},");
        js.AppendLine("      {data:pwBuf.tot}");
        js.AppendLine("    ]");
        js.AppendLine("  });");
        js.AppendLine("}");

        // ── fetch ──────────────────────────────────────────────
        js.AppendLine($"async function cargarDatos(){{");
        js.AppendLine($"  try{{");
        js.AppendLine($"    var r=await fetch('./{dataJsNombre}?t='+Date.now(),{{cache:'no-store'}});");
        js.AppendLine($"    if(!r.ok)return;");
        js.AppendLine($"    var D=await r.json();");
        js.AppendLine($"    updateDashboard(D);");
        js.AppendLine($"  }}catch(e){{console.warn('fetch error:',e);}}");
        js.AppendLine($"}}");

        // ── updateDashboard ────────────────────────────────────
        js.AppendLine("function updateDashboard(D){");

        // KPI
        js.AppendLine("  document.getElementById('kV').textContent=(+D.kV).toFixed(1);");
        js.AppendLine("  document.getElementById('kI').textContent=(+D.kI).toFixed(1);");
        js.AppendLine("  document.getElementById('kKW').textContent=(+D.kKW).toFixed(1);");
        js.AppendLine("  document.getElementById('kFP').textContent=(+D.kFP).toFixed(2);");
        js.AppendLine("  document.getElementById('kTHD').textContent=(+D.kTHD).toFixed(1);");
        js.AppendLine("  document.getElementById('kFRQ').textContent=(+D.kFRQ).toFixed(1);");
        js.AppendLine("  document.getElementById('kDBV').textContent=(+D.kDBV).toFixed(1);");
        js.AppendLine("  document.getElementById('kKVA').textContent=(+D.kKVA).toFixed(1);");

        // Tabla fases
        js.AppendLine("  ['1','2','3'].forEach(function(l){");
        js.AppendLine("    document.getElementById('v'+l).textContent=(+D['v'+l]).toFixed(1);");
        js.AppendLine("    document.getElementById('i'+l).textContent=(+D['i'+l]).toFixed(1);");
        js.AppendLine("    document.getElementById('w'+l).textContent=(+D['w'+l]).toFixed(1);");
        js.AppendLine("    document.getElementById('r'+l).textContent=(+D['r'+l]).toFixed(1);");
        js.AppendLine("    document.getElementById('f'+l).textContent=(+D['f'+l]).toFixed(2);");
        js.AppendLine("  });");
        js.AppendLine("  document.getElementById('vT').textContent=((+D.v1+ +D.v2+ +D.v3)/3).toFixed(1);");
        js.AppendLine("  document.getElementById('iT').textContent=((+D.i1+ +D.i2+ +D.i3)).toFixed(1);");
        js.AppendLine("  document.getElementById('wT').textContent=((+D.w1+ +D.w2+ +D.w3)).toFixed(1);");
        js.AppendLine("  document.getElementById('rT').textContent=((+D.r1+ +D.r2+ +D.r3)).toFixed(1);");
        js.AppendLine("  document.getElementById('fT').textContent=((+D.f1+ +D.f2+ +D.f3)/3).toFixed(2);");

        // Sparklines
        js.AppendLine("  spBuf[0].push(+D.v1);spBuf[0].shift();");
        js.AppendLine("  spBuf[1].push(+D.v2);spBuf[1].shift();");
        js.AppendLine("  spBuf[2].push(+D.v3);spBuf[2].shift();");
        js.AppendLine("  updateSparks();");
        js.AppendLine("  document.getElementById('sv1').textContent=(+D.v1).toFixed(1)+' V';");
        js.AppendLine("  document.getElementById('sv2').textContent=(+D.v2).toFixed(1)+' V';");
        js.AppendLine("  document.getElementById('sv3').textContent=(+D.v3).toFixed(1)+' V';");

        // THD bars
        js.AppendLine("  ['1','2','3'].forEach(function(l){");
        js.AppendLine("    var tv=+D['thdVL'+l],ti=+D['thdIL'+l];");
        js.AppendLine("    document.getElementById('tvl'+l).style.width=(tv*10)+'%';");
        js.AppendLine("    document.getElementById('tvv'+l).textContent=tv.toFixed(1)+'%';");
        js.AppendLine("    document.getElementById('til'+l).style.width=(ti*10)+'%';");
        js.AppendLine("    document.getElementById('tiv'+l).textContent=ti.toFixed(1)+'%';");
        js.AppendLine("  });");

        // Extra THD card
        js.AppendLine("  document.getElementById('dbV').textContent=(+D.kDBV).toFixed(1)+'%';");
        js.AppendLine("  document.getElementById('dbI').textContent=(+D.dbI).toFixed(1)+'%';");
        js.AppendLine("  document.getElementById('frqB').textContent=(+D.kFRQ).toFixed(1)+' Hz';");
        js.AppendLine("  document.getElementById('kvarB').textContent=(+D.kvar).toFixed(1);");

        // Buffers potencia
        js.AppendLine("  var w1=+D.w1,w2=+D.w2,w3=+D.w3,wT=w1+w2+w3;");
        js.AppendLine("  if(!pwBufInit){");
        js.AppendLine("    pwBuf.l1=Array(PW_N).fill(w1);");
        js.AppendLine("    pwBuf.l2=Array(PW_N).fill(w2);");
        js.AppendLine("    pwBuf.l3=Array(PW_N).fill(w3);");
        js.AppendLine("    pwBuf.tot=Array(PW_N).fill(wT);");
        js.AppendLine("    pwBufInit=true;");
        js.AppendLine("  } else {");
        js.AppendLine("    pwBuf.l1.push(w1);pwBuf.l1.shift();");
        js.AppendLine("    pwBuf.l2.push(w2);pwBuf.l2.shift();");
        js.AppendLine("    pwBuf.l3.push(w3);pwBuf.l3.shift();");
        js.AppendLine("    pwBuf.tot.push(wT);pwBuf.tot.shift();");
        js.AppendLine("  }");
        js.AppendLine("  document.getElementById('pwBadge').textContent=wT.toFixed(1)+' kW';");
        js.AppendLine("  refreshPwChart();");

        // Bottom cards
        js.AppendLine("  var kwhVal=+D.kwh;");
        js.AppendLine("  document.getElementById('ecKWH').textContent=kwhVal.toFixed(2)+' kWh';");
        js.AppendLine("  document.getElementById('kwhIco').textContent=kwhVal>0?'▲':'●';");
        js.AppendLine("  document.getElementById('kwhIco').style.color=kwhVal>0?'var(--green)':'var(--txt2)';");
        js.AppendLine("  document.getElementById('kwhTxt').textContent=kwhVal>0?'Consumo registrado':'Sin consumo aún';");
        js.AppendLine("  document.getElementById('ecDEM').textContent=(+D.demanda).toFixed(1)+' kW';");
        js.AppendLine("  document.getElementById('ecCO2').textContent=(+D.co2).toFixed(2)+' kg';");

        js.AppendLine("  colorear(D);");
        js.AppendLine("}");

        // ── Arranque ───────────────────────────────────────────
        js.AppendLine("document.addEventListener('DOMContentLoaded',function(){");
        js.AppendLine("  updateSparks();");
        js.AppendLine("  initPwChart();");
        js.AppendLine("  cargarDatos();");
        js.AppendLine("  setInterval(cargarDatos,2500);");
        js.AppendLine("  window.addEventListener('resize',function(){if(pwChart)pwChart.resize();});");
        js.AppendLine("});");

        File.WriteAllText(rutaAppJs, js.ToString());
    }
    // ── Helper para generar un ítem KPI sin badge ────────────
    private static void KPI(StringBuilder h, string id, string label, string unit)
    {
        h.AppendLine($"  <div class='kpi'>");
        h.AppendLine($"    <div class='kl'>{label}</div>");
        h.AppendLine($"    <div class='kv' id='{id}'>--</div>");
        h.AppendLine($"    <div class='ku'>{unit}</div>");
        h.AppendLine($"  </div>");
    }
}
