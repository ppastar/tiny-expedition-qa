using System;
using System.Collections.Generic;

namespace TinyExpedition.Core.Analysis
{
    /// <summary>
    /// 카이제곱 적합도 검정. 관측 횟수가 기대 확률과 맞는지 본다 (가챠 확률 검증용).
    /// </summary>
    public static class ChiSquare
    {
        public sealed class Result
        {
            public double Statistic { get; }
            public int DegreesOfFreedom { get; }
            public double PValue { get; }

            public Result(double statistic, int df, double pValue)
            {
                Statistic = statistic; DegreesOfFreedom = df; PValue = pValue;
            }
        }

        /// <param name="observed">범주별 관측 횟수</param>
        /// <param name="expectedProbabilities">범주별 기대 확률 (합 1)</param>
        public static Result GoodnessOfFit(IReadOnlyList<long> observed, IReadOnlyList<double> expectedProbabilities)
        {
            if (observed.Count != expectedProbabilities.Count) throw new ArgumentException("범주 수가 다릅니다.");
            long n = 0;
            foreach (var o in observed) n += o;
            if (n == 0) throw new ArgumentException("관측값이 없습니다.");

            double stat = 0;
            int categories = 0;
            for (int i = 0; i < observed.Count; i++)
            {
                double expected = n * expectedProbabilities[i];
                if (expected <= 0)
                {
                    // 기대 0인데 나왔다면 확률 표와 다르게 동작하는 것
                    if (observed[i] > 0) return new Result(double.PositiveInfinity, observed.Count - 1, 0);
                    continue;
                }
                double d = observed[i] - expected;
                stat += d * d / expected;
                categories++;
            }
            int df = Math.Max(1, categories - 1);
            return new Result(stat, df, UpperTailP(stat, df));
        }

        /// <summary>카이제곱 분포 위쪽 꼬리 확률 P(X ≥ x).</summary>
        public static double UpperTailP(double x, int df)
        {
            if (x <= 0) return 1;
            return RegularizedGammaQ(df / 2.0, x / 2.0);
        }

        // Numerical Recipes 방식: Q(a,x) = 1 - P(a,x)
        private static double RegularizedGammaQ(double a, double x)
        {
            if (x < a + 1) return 1 - GammaPSeries(a, x);
            return GammaQContinuedFraction(a, x);
        }

        private static double GammaPSeries(double a, double x)
        {
            double sum = 1.0 / a, del = sum, ap = a;
            for (int n = 0; n < 1000; n++)
            {
                ap += 1;
                del *= x / ap;
                sum += del;
                if (Math.Abs(del) < Math.Abs(sum) * 1e-15) break;
            }
            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }

        private static double GammaQContinuedFraction(double a, double x)
        {
            const double fpmin = 1e-300;
            double b = x + 1 - a, c = 1 / fpmin, d = 1 / b, h = d;
            for (int i = 1; i < 1000; i++)
            {
                double an = -i * (i - a);
                b += 2;
                d = an * d + b; if (Math.Abs(d) < fpmin) d = fpmin;
                c = b + an / c; if (Math.Abs(c) < fpmin) c = fpmin;
                d = 1 / d;
                double del = d * c;
                h *= del;
                if (Math.Abs(del - 1) < 1e-15) break;
            }
            return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
        }

        private static double LogGamma(double x)
        {
            // Lanczos 근사
            double[] cof = { 76.18009172947146, -86.50532032941677, 24.01409824083091,
                             -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5 };
            double y = x, tmp = x + 5.5;
            tmp -= (x + 0.5) * Math.Log(tmp);
            double ser = 1.000000000190015;
            for (int j = 0; j < 6; j++) ser += cof[j] / ++y;
            return -tmp + Math.Log(2.5066282746310005 * ser / x);
        }
    }
}
