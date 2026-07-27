using FunctEngine;
using System;

namespace DistributionFunctions
{
    [FunctEngineExport("Distribution Functions", "Biblioteca de funciones de distribuciones de probabilidad: PDF/PMF, CDF y cuantiles")]
    public static class DistributionLibrary
    {
        // ─── Normal (Gaussian) Distribution ────────────────────────────────────

        [FunctEngineExport("NormalPdf", "Función de densidad de probabilidad Normal en x, dados media y desviación estándar")]
        public static double NormalPdf(double x, double mean = 0, double stdDev = 1)
        {
            if (stdDev <= 0) throw new ArgumentException("La desviación estándar debe ser positiva");
            double z = (x - mean) / stdDev;
            return Math.Exp(-0.5 * z * z) / (stdDev * Math.Sqrt(2 * Math.PI));
        }

        [FunctEngineExport("NormalCdf", "Función de distribución acumulada Normal en x, dados media y desviación estándar")]
        public static double NormalCdf(double x, double mean = 0, double stdDev = 1)
        {
            if (stdDev <= 0) throw new ArgumentException("La desviación estándar debe ser positiva");
            return StandardNormalCdf((x - mean) / stdDev);
        }

        [FunctEngineExport("NormalQuantile", "Cuantil (inversa de la CDF) Normal para una probabilidad p, dados media y desviación estándar")]
        public static double NormalQuantile(double p, double mean = 0, double stdDev = 1)
        {
            ValidateProbability(p);
            if (stdDev <= 0) throw new ArgumentException("La desviación estándar debe ser positiva");
            double z = BisectUnbounded(StandardNormalCdf, p);
            return mean + stdDev * z;
        }

        // ─── Student's t Distribution ──────────────────────────────────────────

        [FunctEngineExport("StudentTPdf", "Función de densidad de probabilidad t de Student en x, con grados de libertad df")]
        public static double StudentTPdf(double x, double df)
        {
            if (df <= 0) throw new ArgumentException("Los grados de libertad deben ser positivos");
            double logPdf = LogGamma((df + 1) / 2.0) - LogGamma(df / 2.0) - 0.5 * Math.Log(df * Math.PI)
                - (df + 1) / 2.0 * Math.Log(1 + x * x / df);
            return Math.Exp(logPdf);
        }

        [FunctEngineExport("StudentTCdf", "Función de distribución acumulada t de Student en x, con grados de libertad df")]
        public static double StudentTCdf(double x, double df)
        {
            if (df <= 0) throw new ArgumentException("Los grados de libertad deben ser positivos");
            double twoTailP = BetaInc(df / 2.0, 0.5, df / (df + x * x));
            return x >= 0 ? 1 - twoTailP / 2.0 : twoTailP / 2.0;
        }

        [FunctEngineExport("StudentTQuantile", "Cuantil (inversa de la CDF) t de Student para una probabilidad p, con grados de libertad df")]
        public static double StudentTQuantile(double p, double df)
        {
            ValidateProbability(p);
            if (df <= 0) throw new ArgumentException("Los grados de libertad deben ser positivos");
            return BisectUnbounded(x => StudentTCdf(x, df), p);
        }

        // ─── Chi-Square Distribution ────────────────────────────────────────────

        [FunctEngineExport("ChiSquarePdf", "Función de densidad de probabilidad chi-cuadrado en x, con grados de libertad df")]
        public static double ChiSquarePdf(double x, double df)
        {
            if (df <= 0) throw new ArgumentException("Los grados de libertad deben ser positivos");
            if (x <= 0) return 0;
            double logPdf = -(df / 2.0) * Math.Log(2) - LogGamma(df / 2.0) + (df / 2.0 - 1) * Math.Log(x) - x / 2.0;
            return Math.Exp(logPdf);
        }

        [FunctEngineExport("ChiSquareCdf", "Función de distribución acumulada chi-cuadrado en x, con grados de libertad df")]
        public static double ChiSquareCdf(double x, double df)
        {
            if (df <= 0) throw new ArgumentException("Los grados de libertad deben ser positivos");
            if (x <= 0) return 0;
            return GammaInc(df / 2.0, x / 2.0);
        }

        [FunctEngineExport("ChiSquareQuantile", "Cuantil (inversa de la CDF) chi-cuadrado para una probabilidad p, con grados de libertad df")]
        public static double ChiSquareQuantile(double p, double df)
        {
            ValidateProbability(p);
            if (df <= 0) throw new ArgumentException("Los grados de libertad deben ser positivos");
            return BisectNonNegative(x => ChiSquareCdf(x, df), p);
        }

        // ─── F Distribution ─────────────────────────────────────────────────────

        [FunctEngineExport("FPdf", "Función de densidad de probabilidad F en x, con grados de libertad df1 y df2")]
        public static double FPdf(double x, double df1, double df2)
        {
            if (df1 <= 0 || df2 <= 0) throw new ArgumentException("Los grados de libertad deben ser positivos");
            if (x <= 0) return 0;
            double logPdf = (df1 / 2.0) * Math.Log(df1 / df2) + (df1 / 2.0 - 1) * Math.Log(x)
                - ((df1 + df2) / 2.0) * Math.Log(1 + df1 * x / df2) - LogBeta(df1 / 2.0, df2 / 2.0);
            return Math.Exp(logPdf);
        }

        [FunctEngineExport("FCdf", "Función de distribución acumulada F en x, con grados de libertad df1 y df2")]
        public static double FCdf(double x, double df1, double df2)
        {
            if (df1 <= 0 || df2 <= 0) throw new ArgumentException("Los grados de libertad deben ser positivos");
            if (x <= 0) return 0;
            double z = df1 * x / (df1 * x + df2);
            return BetaInc(df1 / 2.0, df2 / 2.0, z);
        }

        [FunctEngineExport("FQuantile", "Cuantil (inversa de la CDF) F para una probabilidad p, con grados de libertad df1 y df2")]
        public static double FQuantile(double p, double df1, double df2)
        {
            ValidateProbability(p);
            if (df1 <= 0 || df2 <= 0) throw new ArgumentException("Los grados de libertad deben ser positivos");
            return BisectNonNegative(x => FCdf(x, df1, df2), p);
        }

        // ─── Uniform Distribution ────────────────────────────────────────────────

        [FunctEngineExport("UniformPdf", "Función de densidad de probabilidad Uniforme continua en x, entre min y max")]
        public static double UniformPdf(double x, double min = 0, double max = 1)
        {
            if (max <= min) throw new ArgumentException("max debe ser mayor que min");
            return x >= min && x <= max ? 1.0 / (max - min) : 0;
        }

        [FunctEngineExport("UniformCdf", "Función de distribución acumulada Uniforme continua en x, entre min y max")]
        public static double UniformCdf(double x, double min = 0, double max = 1)
        {
            if (max <= min) throw new ArgumentException("max debe ser mayor que min");
            if (x < min) return 0;
            if (x > max) return 1;
            return (x - min) / (max - min);
        }

        [FunctEngineExport("UniformQuantile", "Cuantil (inversa de la CDF) Uniforme continua para una probabilidad p, entre min y max")]
        public static double UniformQuantile(double p, double min = 0, double max = 1)
        {
            ValidateProbability(p);
            if (max <= min) throw new ArgumentException("max debe ser mayor que min");
            return min + p * (max - min);
        }

        // ─── Exponential Distribution ───────────────────────────────────────────

        [FunctEngineExport("ExponentialPdf", "Función de densidad de probabilidad Exponencial en x, con parámetro de tasa (rate = 1/media)")]
        public static double ExponentialPdf(double x, double rate = 1)
        {
            if (rate <= 0) throw new ArgumentException("La tasa debe ser positiva");
            return x >= 0 ? rate * Math.Exp(-rate * x) : 0;
        }

        [FunctEngineExport("ExponentialCdf", "Función de distribución acumulada Exponencial en x, con parámetro de tasa (rate = 1/media)")]
        public static double ExponentialCdf(double x, double rate = 1)
        {
            if (rate <= 0) throw new ArgumentException("La tasa debe ser positiva");
            return x >= 0 ? 1 - Math.Exp(-rate * x) : 0;
        }

        [FunctEngineExport("ExponentialQuantile", "Cuantil (inversa de la CDF) Exponencial para una probabilidad p, con parámetro de tasa (rate = 1/media)")]
        public static double ExponentialQuantile(double p, double rate = 1)
        {
            ValidateProbability(p);
            if (rate <= 0) throw new ArgumentException("La tasa debe ser positiva");
            return -Math.Log(1 - p) / rate;
        }

        // ─── Gamma Distribution ──────────────────────────────────────────────────

        [FunctEngineExport("GammaPdf", "Función de densidad de probabilidad Gamma en x, con parámetros de forma (shape) y escala (scale)")]
        public static double GammaPdf(double x, double shape, double scale = 1)
        {
            if (shape <= 0 || scale <= 0) throw new ArgumentException("La forma y la escala deben ser positivas");
            if (x <= 0) return 0;
            double logPdf = (shape - 1) * Math.Log(x) - x / scale - LogGamma(shape) - shape * Math.Log(scale);
            return Math.Exp(logPdf);
        }

        [FunctEngineExport("GammaCdf", "Función de distribución acumulada Gamma en x, con parámetros de forma (shape) y escala (scale)")]
        public static double GammaCdf(double x, double shape, double scale = 1)
        {
            if (shape <= 0 || scale <= 0) throw new ArgumentException("La forma y la escala deben ser positivas");
            if (x <= 0) return 0;
            return GammaInc(shape, x / scale);
        }

        [FunctEngineExport("GammaQuantile", "Cuantil (inversa de la CDF) Gamma para una probabilidad p, con parámetros de forma (shape) y escala (scale)")]
        public static double GammaQuantile(double p, double shape, double scale = 1)
        {
            ValidateProbability(p);
            if (shape <= 0 || scale <= 0) throw new ArgumentException("La forma y la escala deben ser positivas");
            return BisectNonNegative(x => GammaCdf(x, shape, scale), p);
        }

        // ─── Beta Distribution ───────────────────────────────────────────────────

        [FunctEngineExport("BetaPdf", "Función de densidad de probabilidad Beta en x (0 a 1), con parámetros alpha y beta")]
        public static double BetaPdf(double x, double alpha, double beta)
        {
            if (alpha <= 0 || beta <= 0) throw new ArgumentException("Alpha y beta deben ser positivos");
            if (x <= 0 || x >= 1) return 0;
            double logPdf = (alpha - 1) * Math.Log(x) + (beta - 1) * Math.Log(1 - x) - LogBeta(alpha, beta);
            return Math.Exp(logPdf);
        }

        [FunctEngineExport("BetaCdf", "Función de distribución acumulada Beta en x (0 a 1), con parámetros alpha y beta")]
        public static double BetaCdf(double x, double alpha, double beta)
        {
            if (alpha <= 0 || beta <= 0) throw new ArgumentException("Alpha y beta deben ser positivos");
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            return BetaInc(alpha, beta, x);
        }

        [FunctEngineExport("BetaQuantile", "Cuantil (inversa de la CDF) Beta para una probabilidad p, con parámetros alpha y beta")]
        public static double BetaQuantile(double p, double alpha, double beta)
        {
            ValidateProbability(p);
            if (alpha <= 0 || beta <= 0) throw new ArgumentException("Alpha y beta deben ser positivos");
            return BisectBounded(x => BetaCdf(x, alpha, beta), p, 0, 1);
        }

        // ─── Binomial Distribution ───────────────────────────────────────────────

        [FunctEngineExport("BinomialPmf", "Función de masa de probabilidad Binomial: P(X = k) para n ensayos con probabilidad de éxito p")]
        public static double BinomialPmf(int k, int n, double p)
        {
            if (n < 0) throw new ArgumentException("n debe ser no negativo");
            if (p < 0 || p > 1) throw new ArgumentException("p debe estar entre 0 y 1");
            if (k < 0 || k > n) return 0;
            double logPmf = LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1)
                + (k > 0 ? k * Math.Log(p) : 0) + (n - k > 0 ? (n - k) * Math.Log(1 - p) : 0);
            return Math.Exp(logPmf);
        }

        [FunctEngineExport("BinomialCdf", "Función de distribución acumulada Binomial: P(X <= k) para n ensayos con probabilidad de éxito p")]
        public static double BinomialCdf(int k, int n, double p)
        {
            if (n < 0) throw new ArgumentException("n debe ser no negativo");
            if (p < 0 || p > 1) throw new ArgumentException("p debe estar entre 0 y 1");
            if (k < 0) return 0;
            if (k >= n) return 1;
            double sum = 0;
            for (int i = 0; i <= k; i++) sum += BinomialPmf(i, n, p);
            return Math.Min(1, sum);
        }

        [FunctEngineExport("BinomialQuantile", "Cuantil (inversa de la CDF) Binomial: el menor k tal que P(X <= k) >= prob")]
        public static int BinomialQuantile(double prob, int n, double p)
        {
            ValidateProbability(prob);
            if (n < 0) throw new ArgumentException("n debe ser no negativo");
            if (p < 0 || p > 1) throw new ArgumentException("p debe estar entre 0 y 1");
            double cumulative = 0;
            for (int k = 0; k <= n; k++)
            {
                cumulative += BinomialPmf(k, n, p);
                if (cumulative >= prob) return k;
            }
            return n;
        }

        // ─── Poisson Distribution ────────────────────────────────────────────────

        [FunctEngineExport("PoissonPmf", "Función de masa de probabilidad Poisson: P(X = k) con tasa media lambda")]
        public static double PoissonPmf(int k, double lambda)
        {
            if (lambda <= 0) throw new ArgumentException("Lambda debe ser positiva");
            if (k < 0) return 0;
            double logPmf = -lambda + k * Math.Log(lambda) - LogGamma(k + 1);
            return Math.Exp(logPmf);
        }

        [FunctEngineExport("PoissonCdf", "Función de distribución acumulada Poisson: P(X <= k) con tasa media lambda")]
        public static double PoissonCdf(int k, double lambda)
        {
            if (lambda <= 0) throw new ArgumentException("Lambda debe ser positiva");
            if (k < 0) return 0;
            double sum = 0;
            for (int i = 0; i <= k; i++) sum += PoissonPmf(i, lambda);
            return Math.Min(1, sum);
        }

        [FunctEngineExport("PoissonQuantile", "Cuantil (inversa de la CDF) Poisson: el menor k tal que P(X <= k) >= prob")]
        public static int PoissonQuantile(double prob, double lambda)
        {
            ValidateProbability(prob);
            if (lambda <= 0) throw new ArgumentException("Lambda debe ser positiva");
            int maxK = Math.Max(1000, (int)(lambda + 20 * Math.Sqrt(lambda) + 20));
            double cumulative = 0;
            for (int k = 0; k <= maxK; k++)
            {
                cumulative += PoissonPmf(k, lambda);
                if (cumulative >= prob) return k;
            }
            return maxK;
        }

        // ─── Internal helpers ────────────────────────────────────────────────────

        private static void ValidateProbability(double p)
        {
            if (p <= 0 || p >= 1)
                throw new ArgumentException("La probabilidad p debe estar estrictamente entre 0 y 1");
        }

        // Búsqueda binaria adaptativa sobre toda la recta real (para distribuciones
        // simétricas o de soporte no acotado, como Normal y t de Student).
        private static double BisectUnbounded(Func<double, double> cdf, double p)
        {
            double lo = -1, hi = 1;
            while (cdf(lo) > p) lo *= 2;
            while (cdf(hi) < p) hi *= 2;
            for (int i = 0; i < 200; i++)
            {
                double mid = (lo + hi) / 2;
                if (cdf(mid) < p) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        // Búsqueda binaria adaptativa sobre [0, ∞) (para Chi-cuadrado, F, Gamma).
        private static double BisectNonNegative(Func<double, double> cdf, double p)
        {
            double lo = 0, hi = 1;
            while (cdf(hi) < p) hi *= 2;
            for (int i = 0; i < 200; i++)
            {
                double mid = (lo + hi) / 2;
                if (cdf(mid) < p) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        // Búsqueda binaria acotada sobre [lo, hi] (para Beta, soporte en [0, 1]).
        private static double BisectBounded(Func<double, double> cdf, double p, double lo, double hi)
        {
            for (int i = 0; i < 200; i++)
            {
                double mid = (lo + hi) / 2;
                if (cdf(mid) < p) lo = mid; else hi = mid;
            }
            return (lo + hi) / 2;
        }

        private static double StandardNormalCdf(double z)
        {
            double t = 1.0 / (1.0 + 0.2316419 * Math.Abs(z));
            double d = 0.3989422820 * Math.Exp(-z * z / 2.0);
            double poly = t * (0.3193815 + t * (-0.3565638 + t * (1.7814779 + t * (-1.8212560 + t * 1.3302744))));
            double prob = 1.0 - d * poly;
            return z >= 0 ? prob : 1.0 - prob;
        }

        private static double LogGamma(double x)
        {
            double[] c = { 76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 1.208650973866179e-3, -5.395239384953e-6 };
            double y = x;
            double tmp = x + 5.5 - (x + 0.5) * Math.Log(x + 5.5);
            double ser = 1.000000000190015;
            for (int j = 0; j < 6; j++) ser += c[j] / ++y;
            return -tmp + Math.Log(2.5066282746310005 * ser / x);
        }

        private static double LogBeta(double a, double b) => LogGamma(a) + LogGamma(b) - LogGamma(a + b);

        private static double BetaCF(double a, double b, double x)
        {
            const double fpmin = 1e-30;
            double qab = a + b, qap = a + 1.0, qam = a - 1.0;
            double c = 1.0, d = 1.0 - qab * x / qap;
            if (Math.Abs(d) < fpmin) d = fpmin;
            d = 1.0 / d;
            double h = d;
            for (int m = 1; m <= 200; m++)
            {
                int m2 = 2 * m;
                double aa = m * (b - m) * x / ((qam + m2) * (a + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
                c = 1.0 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
                d = 1.0 / d; h *= d * c;
                aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
                d = 1.0 + aa * d; if (Math.Abs(d) < fpmin) d = fpmin;
                c = 1.0 + aa / c; if (Math.Abs(c) < fpmin) c = fpmin;
                d = 1.0 / d;
                double del = d * c; h *= del;
                if (Math.Abs(del - 1.0) <= 3e-7) break;
            }
            return h;
        }

        private static double BetaInc(double a, double b, double x)
        {
            if (x <= 0) return 0;
            if (x >= 1) return 1;
            double lbeta = LogGamma(a + b) - LogGamma(a) - LogGamma(b);
            double bt = Math.Exp(lbeta + a * Math.Log(x) + b * Math.Log(1.0 - x));
            return x < (a + 1.0) / (a + b + 2.0)
                ? bt * BetaCF(a, b, x) / a
                : 1.0 - bt * BetaCF(b, a, 1.0 - x) / b;
        }

        private static double GammaIncSeries(double a, double x)
        {
            double sum = 1.0 / a, del = 1.0 / a, ap = a;
            for (int n = 1; n <= 200; n++) { ap++; del *= x / ap; sum += del; if (Math.Abs(del) < Math.Abs(sum) * 3e-7) break; }
            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }

        private static double GammaIncCF(double a, double x)
        {
            const double fpmin = 1e-30;
            double b = x + 1.0 - a, c = 1.0 / fpmin, d = 1.0 / b, h = d;
            for (int i = 1; i <= 200; i++)
            {
                double an = -i * (i - a); b += 2.0;
                d = an * d + b; if (Math.Abs(d) < fpmin) d = fpmin;
                c = b + an / c; if (Math.Abs(c) < fpmin) c = fpmin;
                d = 1.0 / d; double del = d * c; h *= del;
                if (Math.Abs(del - 1.0) < 3e-7) break;
            }
            return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
        }

        private static double GammaInc(double a, double x)
        {
            if (x <= 0) return 0;
            return x < a + 1.0 ? GammaIncSeries(a, x) : 1.0 - GammaIncCF(a, x);
        }
    }
}
