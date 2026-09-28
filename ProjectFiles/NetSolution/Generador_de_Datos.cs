#region Using directives
using System;
using UAManagedCore;
using OpcUa = UAManagedCore.OpcUa;
using FTOptix.UI;
using FTOptix.HMIProject;
using FTOptix.NativeUI;
using FTOptix.Retentivity;
using FTOptix.CoreBase;
using FTOptix.Core;
using FTOptix.NetLogic;
using FTOptix.WebUI;
using FTOptix.OPCUAServer;
#endregion

public class Generador_de_Datos : BaseNetLogic
{
    public int Seed;
    public int PeriodoMs = 1000;

    private PeriodicTask _task;
    private Random _rng;
    private double _phase;
    private int _tick;

    private double _energiaAnterior;
    private double _demandaAnterior;
    private double _voltajeObjetivo;
    private double _corrienteObjetivo;
    private double _fpObjetivo;

    private double _h3Obj, _h5Obj, _h7Obj, _h9Obj;
    private int _holdH3, _holdH5, _holdH7, _holdH9;

    private int _holdVoltaje;
    private int _holdCorriente;
    private int _holdFP;

    private IUAObject parentLine;

    // ==== Valores base del comportamiento (usados como referencia por fase) ====
    public double Voltaje { get; private set; }
    public double Corriente { get; private set; }
    public double Factor_de_Potencia { get; private set; }
    public double Frecuencia { get; private set; }

    // ==== Totales trifásicos (derivados de la suma de fases) ====
    public double Potencia_Activa { get; private set; }
    public double Potencia_Reactiva { get; private set; }
    public double Potencia_Aparente { get; private set; }
    public double Potencia_Reactiva_Ind { get; private set; }
    public double Potencia_Reactiva_Cap { get; private set; }

    // ==== Voltajes línea-línea (derivados de fase-neutro) ====
    public double Voltaje_L1_L2 { get; private set; }
    public double Voltaje_L2_L3 { get; private set; }
    public double Voltaje_L3_L1 { get; private set; }

    // ==== Armónicos y calidad de energía ====
    public double THD_Voltaje { get; private set; }
    public double THD_Corriente { get; private set; }
    public double Armonico_Principal { get; private set; }
    public double Armonico_3 { get; private set; }
    public double Armonico_5 { get; private set; }
    public double Armonico_7 { get; private set; }
    public double Armonico_9 { get; private set; }

    // ==== Energía / demanda ====
    public double Energia_Activa_Importada_Total { get; private set; }
    public double Emisiones_de_la_Energia_Importada { get; private set; }
    public double Demanda_Potencia_Activa { get; private set; }

    public int Control { get; set; }

    public override void Start()
    {
        parentLine = (IUAObject)LogicObject.Owner;
        Seed = LogicObject.GetVariable("Seed").Value;
        _rng = new Random(Seed);
        _phase = _rng.NextDouble() * Math.PI * 2.0;
        _tick = 0;
        _energiaAnterior = 0.0;
        _demandaAnterior = 0.0;
        InicializarValores();
        _task = new PeriodicTask(Actualizar, PeriodoMs, LogicObject);
        _task.Start();
    }

    public override void Stop()
    {
        _task?.Dispose();
        _task = null;
    }

    private void Actualizar()
    {
        _tick++;
        double u = Clamp(Control / 100.0, -1.0, 1.0);
        double dtSeconds = PeriodoMs / 1000.0;

        // ===== VOLTAJE BASE =====
        if (_holdVoltaje-- <= 0)
        {
            double salto = Noise(2.5);
            if (_rng.NextDouble() < 0.18) salto += Noise(6.0);
            _voltajeObjetivo = Clamp(Voltaje + salto - (u * 5), 210.0, 245.0);
            _holdVoltaje = _rng.Next(4, 12);
        }
        Voltaje = Clamp(Voltaje + ((_voltajeObjetivo - Voltaje) * 0.35) + Noise(0.25), 210.0, 245.0);

        // ===== CORRIENTE BASE =====
        if (_holdCorriente-- <= 0)
        {
            double salto = Noise(4.0) + (u * 10);
            if (_rng.NextDouble() < 0.25) salto += Noise(10.0);
            _corrienteObjetivo = Clamp(Corriente + salto, 2.0, 90.0);
            _holdCorriente = _rng.Next(2, 7);
        }
        Corriente = Clamp(Corriente + ((_corrienteObjetivo - Corriente) * 0.28) + Noise(0.6), 2.0, 90.0);

        // ===== FP BASE =====
        if (_holdFP-- <= 0)
        {
            double salto = Noise(0.015) - (u * 2.5);
            if (_rng.NextDouble() < 0.12) salto -= 0.04;
            _fpObjetivo = Clamp(Factor_de_Potencia + salto, 0.70, 0.99);
            _holdFP = _rng.Next(5, 14);
        }
        Factor_de_Potencia = Clamp(Factor_de_Potencia + ((_fpObjetivo - Factor_de_Potencia) * 0.25) + Noise(0.004), 0.70, 0.99);

        Frecuencia = Clamp(60.0 + Noise(0.03), 59.85, 60.15);

        // ===== TIPO DE CARGA (inductiva/capacitiva) — una sola decisión por tick =====
        bool capacitiva = _rng.NextDouble() < (0.10 + (u < 0 ? 2.5 : 0.0));
        double qSign = capacitiva ? -1.0 : 1.0;

        // =============================================================
        // VALORES POR FASE: desbalance real a partir de los valores base
        // Cada fase tiene su propia V, I y FP independiente, pero
        // correlacionada con la base → respeta la física.
        // =============================================================

        // Voltajes fase-neutro (imbalance típico ≤ 2 V)
        double vL1 = Clamp(Voltaje + Noise(1.5), 210.0, 245.0);
        double vL2 = Clamp(Voltaje + Noise(1.5), 210.0, 245.0);
        double vL3 = Clamp(Voltaje + Noise(1.5), 210.0, 245.0);

        // Corrientes por fase (desbalance moderado, como carga real)
        double iL1 = Clamp(Corriente + Noise(3.0), 2.0, 90.0);
        double iL2 = Clamp(Corriente + Noise(1.5), 2.0, 90.0);
        double iL3 = Clamp(Corriente + Noise(2.0), 2.0, 90.0);

        // FP por fase (pequeña variación alrededor del base)
        double fpL1 = Clamp(Factor_de_Potencia + Noise(0.012), 0.70, 0.99);
        double fpL2 = Clamp(Factor_de_Potencia + Noise(0.012), 0.70, 0.99);
        double fpL3 = Clamp(Factor_de_Potencia + Noise(0.012), 0.70, 0.99);

        // Potencia aparente por fase [kVA] = V_fase * I_fase
        double sL1 = (vL1 * iL1) / 1000.0;
        double sL2 = (vL2 * iL2) / 1000.0;
        double sL3 = (vL3 * iL3) / 1000.0;

        // Potencia activa por fase [kW] = S * FP
        double pL1 = sL1 * fpL1;
        double pL2 = sL2 * fpL2;
        double pL3 = sL3 * fpL3;

        // Potencia reactiva por fase [kVAr] = S * sin(acos(FP)), con signo
        double qL1 = sL1 * Math.Sin(Math.Acos(fpL1)) * qSign;
        double qL2 = sL2 * Math.Sin(Math.Acos(fpL2)) * qSign;
        double qL3 = sL3 * Math.Sin(Math.Acos(fpL3)) * qSign;

        // =============================================================
        // TOTALES TRIFÁSICOS — derivados de la suma de fases
        // P_III = P_L1 + P_L2 + P_L3  → siempre correcto por definición
        // S_III = S_L1 + S_L2 + S_L3  → suma aritmética (norma IEC para desbalance)
        // FP_III = P_III / S_III
        // Q_III = Q_L1 + Q_L2 + Q_L3
        // =============================================================
        Potencia_Activa = pL1 + pL2 + pL3;
        Potencia_Aparente = sL1 + sL2 + sL3;
        double qIII = qL1 + qL2 + qL3;

        // FP trifásico derivado (no sobreescribe la dinámica base)
        double fpIII = Clamp(Potencia_Activa / Potencia_Aparente, 0.0, 1.0);

        Potencia_Reactiva = Math.Abs(qIII);
        Potencia_Reactiva_Ind = capacitiva ? 0.0 : Math.Abs(qIII);
        Potencia_Reactiva_Cap = capacitiva ? Math.Abs(qIII) : 0.0;

        // Energía y demanda
        Energia_Activa_Importada_Total = _energiaAnterior + (Potencia_Activa * dtSeconds / 3600.0);
        Demanda_Potencia_Activa = Math.Max(Potencia_Activa, _demandaAnterior * 0.995 + Potencia_Activa * 0.005);
        _energiaAnterior = Energia_Activa_Importada_Total;
        _demandaAnterior = Demanda_Potencia_Activa;

        Emisiones_de_la_Energia_Importada = Energia_Activa_Importada_Total * 0.42;

        // ===== VOLTAJES LÍNEA-LÍNEA =====
        // Fórmula fasorial exacta para 120° entre fases:
        // |V_AB| = sqrt(V_A² + V_B² + V_A·V_B)
        Voltaje_L1_L2 = Math.Sqrt(vL1 * vL1 + vL2 * vL2 + vL1 * vL2);
        Voltaje_L2_L3 = Math.Sqrt(vL2 * vL2 + vL3 * vL3 + vL2 * vL3);
        Voltaje_L3_L1 = Math.Sqrt(vL3 * vL3 + vL1 * vL1 + vL3 * vL1);

        // ===== THD Y ARMÓNICOS =====
        // Espectro estable, decreciente y coherente con carga/FP.
        // La fundamental se mantiene cerca de 100% y los armónicos siguen
        // una envolvente física: H3 > H5 > H7 > H9.

        // Tiempo base para oscilaciones lentas
        // Tiempo base para oscilaciones lentas
        double t = _tick * (PeriodoMs / 1000.0);

        // Régimen lento de la red: cambia de forma suave
        double cicloLento = 0.5 + 0.5 * Math.Sin(t * 0.02);   // muy lento
        double cicloMedio = 0.5 + 0.5 * Math.Sin(t * 0.07);   // un poco más rápido

        // Severidad global de distorsión
        double cargaNorm = Clamp(Corriente / 90.0, 0.0, 1.0);
        double severidad = Clamp(
            0.18
            + (1.0 - Factor_de_Potencia) * 0.65
            + cargaNorm * 0.25
            + Noise(0.03),
            0.0, 1.0);

        // Objetivos dinámicos con distinta sensibilidad por armónico
        double objetivoH3 = Clamp(1.2 + severidad * 4.2 + cicloLento * 0.9, 0.8, 8.0);
        double objetivoH5 = Clamp(0.8 + severidad * 3.0 + cicloMedio * 0.6, 0.5, 6.0);
        double objetivoH7 = Clamp(0.5 + severidad * 2.1 + cicloLento * 0.4, 0.3, 4.5);
        double objetivoH9 = Clamp(0.25 + severidad * 1.3 + cicloMedio * 0.25, 0.1, 3.0);

        // Mantener separación física entre armónicos
        objetivoH5 = Math.Min(objetivoH5, objetivoH3 - 0.25);
        objetivoH7 = Math.Min(objetivoH7, objetivoH5 - 0.20);
        objetivoH9 = Math.Min(objetivoH9, objetivoH7 - 0.15);

        // Suavizado lento hacia el objetivo
        _h3Obj = StepLike(_h3Obj, objetivoH3, ref _holdH3, 0.25);
        _h5Obj = StepLike(_h5Obj, objetivoH5, ref _holdH5, 0.22);
        _h7Obj = StepLike(_h7Obj, objetivoH7, ref _holdH7, 0.20);
        _h9Obj = StepLike(_h9Obj, objetivoH9, ref _holdH9, 0.18);

        // Armónicos finales con poca variación
        Armonico_3 = Clamp(Armonico_3 + ((_h3Obj - Armonico_3) * 0.35) + Noise(0.04), 0.8, 8.0);
        Armonico_5 = Clamp(Armonico_5 + ((_h5Obj - Armonico_5) * 0.30) + Noise(0.03), 0.5, 6.0);
        Armonico_7 = Clamp(Armonico_7 + ((_h7Obj - Armonico_7) * 0.28) + Noise(0.025), 0.3, 4.5);
        Armonico_9 = Clamp(Armonico_9 + ((_h9Obj - Armonico_9) * 0.25) + Noise(0.02), 0.1, 3.0);

        // THD de corriente derivado del espectro
        THD_Corriente = Clamp(Math.Sqrt(
            Armonico_3 * Armonico_3 +
            Armonico_5 * Armonico_5 +
            Armonico_7 * Armonico_7 +
            Armonico_9 * Armonico_9), 5.0, 35.0);

        // THD de voltaje relacionado, pero más bajo y estable
        THD_Voltaje = Clamp(0.85 + (THD_Corriente * 0.07) + Noise(0.10), 0.5, 8.0);

        // La fundamental debe quedar casi fija cerca de 100%
        Armonico_Principal = Clamp(100.0 - (THD_Corriente * 0.05) + Noise(0.10), 97.0, 100.0);

        // =============================================================
        // ESCRITURA DE TAGS
        // Los valores por fase ya tienen independencia real (V, I, FP
        // propios). Solo se añade ruido fino de medición donde aplica.
        // =============================================================

        parentLine.GetVariable("Voltaje L1-N").Value = vL1;
        parentLine.GetVariable("Voltaje L2-N").Value = vL2;
        parentLine.GetVariable("Voltaje L3-N").Value = vL3;

        parentLine.GetVariable("Corriente L1").Value = iL1;
        parentLine.GetVariable("Corriente L2").Value = iL2;
        parentLine.GetVariable("Corriente L3").Value = iL3;

        // Potencias activas — la suma SIEMPRE iguala Potencia Activa III
        parentLine.GetVariable("Potencia Activa L1").Value = pL1;
        parentLine.GetVariable("Potencia Activa L2").Value = pL2;
        parentLine.GetVariable("Potencia Activa L3").Value = pL3;

        // Potencias reactivas por fase (magnitud)
        parentLine.GetVariable("Potencia Reactiva L1").Value = Math.Abs(qL1);
        parentLine.GetVariable("Potencia Reactiva L2").Value = Math.Abs(qL2);
        parentLine.GetVariable("Potencia Reactiva L3").Value = Math.Abs(qL3);

        parentLine.GetVariable("Factor de Potencia L1").Value = fpL1;
        parentLine.GetVariable("Factor de Potencia L2").Value = fpL2;
        parentLine.GetVariable("Factor de Potencia L3").Value = fpL3;

        // THD: mismo origen, leve ruido de medición por fase
        parentLine.GetVariable("THD Voltaje L1").Value = Clamp(THD_Voltaje + Noise(0.3), 0.0, 10.0);
        parentLine.GetVariable("THD Voltaje L2").Value = Clamp(THD_Voltaje + Noise(0.3), 0.0, 10.0);
        parentLine.GetVariable("THD Voltaje L3").Value = Clamp(THD_Voltaje + Noise(0.3), 0.0, 10.0);
        parentLine.GetVariable("THD Corriente L1").Value = Clamp(THD_Corriente + Noise(1.0), 0.0, 40.0);
        parentLine.GetVariable("THD Corriente L2").Value = Clamp(THD_Corriente + Noise(1.0), 0.0, 40.0);
        parentLine.GetVariable("THD Corriente L3").Value = Clamp(THD_Corriente + Noise(1.0), 0.0, 40.0);

        // Fundamental: casi igual en las 3 fases, con una variación mínima
        parentLine.GetVariable("Armonico Fundamenta L1").Value = Clamp(Armonico_Principal + Noise(0.08), 97.0, 100.0);
        parentLine.GetVariable("Armonico Fundamenta L2").Value = Clamp(Armonico_Principal + Noise(0.08), 97.0, 100.0);
        parentLine.GetVariable("Armonico Fundamenta L3").Value = Clamp(Armonico_Principal + Noise(0.08), 97.0, 100.0);

        // H3: el más alto
        parentLine.GetVariable("Armonico 3 L1").Value = Clamp(Armonico_3 * 1.04 + Noise(0.04), 0.0, 20.0);
        parentLine.GetVariable("Armonico 3 L2").Value = Clamp(Armonico_3 * 0.99 + Noise(0.04), 0.0, 20.0);
        parentLine.GetVariable("Armonico 3 L3").Value = Clamp(Armonico_3 * 1.02 + Noise(0.04), 0.0, 20.0);

        // H5
        parentLine.GetVariable("Armonico 5 L1").Value = Clamp(Armonico_5 * 1.03 + Noise(0.03), 0.0, 20.0);
        parentLine.GetVariable("Armonico 5 L2").Value = Clamp(Armonico_5 * 0.98 + Noise(0.03), 0.0, 20.0);
        parentLine.GetVariable("Armonico 5 L3").Value = Clamp(Armonico_5 * 1.01 + Noise(0.03), 0.0, 20.0);

        // H7
        parentLine.GetVariable("Armonico 7 L1").Value = Clamp(Armonico_7 * 1.02 + Noise(0.02), 0.0, 15.0);
        parentLine.GetVariable("Armonico 7 L2").Value = Clamp(Armonico_7 * 0.99 + Noise(0.02), 0.0, 15.0);
        parentLine.GetVariable("Armonico 7 L3").Value = Clamp(Armonico_7 * 1.01 + Noise(0.02), 0.0, 15.0);

        // H9
        parentLine.GetVariable("Armonico 9 L1").Value = Clamp(Armonico_9 * 1.02 + Noise(0.015), 0.0, 10.0);
        parentLine.GetVariable("Armonico 9 L2").Value = Clamp(Armonico_9 * 0.99 + Noise(0.015), 0.0, 10.0);
        parentLine.GetVariable("Armonico 9 L3").Value = Clamp(Armonico_9 * 1.01 + Noise(0.015), 0.0, 10.0);

        // Voltajes línea-línea (derivados, sin ruido extra — ya tienen desbalance real)
        parentLine.GetVariable("Voltaje L1-L2").Value = Voltaje_L1_L2;
        parentLine.GetVariable("Voltaje L2-L3").Value = Voltaje_L2_L3;
        parentLine.GetVariable("Voltaje L3-L1").Value = Voltaje_L3_L1;

        // Energía / emisiones / demanda
        parentLine.GetVariable("Energia Activa Importada Total").Value = Energia_Activa_Importada_Total;
        parentLine.GetVariable("Emisiones de la Energia Importada").Value = Emisiones_de_la_Energia_Importada;
        parentLine.GetVariable("Demanda Potencia Activa").Value = Demanda_Potencia_Activa;

        // Totales trifásicos — coherentes por construcción
        parentLine.GetVariable("Potencia Activa III").Value = Potencia_Activa;    // = pL1+pL2+pL3 exacto
        parentLine.GetVariable("Potencia Aparente III").Value = Potencia_Aparente;  // = sL1+sL2+sL3 exacto
        parentLine.GetVariable("Potencia Reactiva Inductiva III").Value = Potencia_Reactiva_Ind;
        parentLine.GetVariable("Potencia Reactiva Capacitiva III").Value = Potencia_Reactiva_Cap;
        parentLine.GetVariable("Factor de Potencia III").Value = fpIII;             // = P_III / S_III exacto
        parentLine.GetVariable("Frecuencia").Value = Frecuencia;
    }

    private void InicializarValores()
    {
        Voltaje = 275.0 + _rng.NextDouble() * 15.0;
        Corriente = 10.0 + _rng.NextDouble() * 25.0;
        Factor_de_Potencia = 0.82 + _rng.NextDouble() * 0.15;
        Frecuencia = 60.0 + Noise(0.05);

        THD_Voltaje = 1.0 + _rng.NextDouble() * 0.8;
        THD_Corriente = 5.5 + _rng.NextDouble() * 3.0;

        Potencia_Activa = 0.0;
        Potencia_Reactiva = 0.0;
        Potencia_Aparente = 0.0;
        Potencia_Reactiva_Ind = 0.0;
        Potencia_Reactiva_Cap = 0.0;
        Energia_Activa_Importada_Total = 0.0;
        Emisiones_de_la_Energia_Importada = 0.0;
        Demanda_Potencia_Activa = 0.0;

        Voltaje_L1_L2 = Voltaje * Math.Sqrt(3.0);
        Voltaje_L2_L3 = Voltaje * Math.Sqrt(3.0);
        Voltaje_L3_L1 = Voltaje * Math.Sqrt(3.0);

        _voltajeObjetivo = Voltaje;
        _corrienteObjetivo = Corriente;
        _fpObjetivo = Factor_de_Potencia;

        _holdVoltaje = _rng.Next(3, 8);
        _holdCorriente = _rng.Next(3, 8);
        _holdFP = _rng.Next(3, 8);

        Armonico_Principal = 98.5 + _rng.NextDouble() * 1.0;
        Armonico_3 = 1.5 + _rng.NextDouble() * 1.0;
        Armonico_5 = 1.0 + _rng.NextDouble() * 0.8;
        Armonico_7 = 0.6 + _rng.NextDouble() * 0.5;
        Armonico_9 = 0.3 + _rng.NextDouble() * 0.3;

        _h3Obj = Armonico_3;
        _h5Obj = Armonico_5;
        _h7Obj = Armonico_7;
        _h9Obj = Armonico_9;

        _holdH3 = _rng.Next(5, 14);
        _holdH5 = _rng.Next(5, 14);
        _holdH7 = _rng.Next(5, 14);
        _holdH9 = _rng.Next(5, 14);
    }

    private double Noise(double amplitude)
    {
        return (_rng.NextDouble() - 0.5) * 2.0 * amplitude;
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
    private double StepLike(double current, double target, ref int hold, double speed)
    {
        if (hold-- <= 0)
        {
            hold = _rng.Next(6, 18); // mantiene valor un rato (plateau)
        }

        // Movimiento suave hacia target
        current += (target - current) * speed;

        // Micro ruido para que no sea plano perfecto
        current += Noise(0.05);

        return current;
    }
    [ExportMethod]
    public void CambioControl(int nuevoControl)
    {
        Control = nuevoControl;
        LogicObject.GetVariable("Control").Value = Control;
    }
}
