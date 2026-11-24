using System;
using System.Globalization;

namespace FieldTakHub.Builder.Services.MapPrint;

/// <summary>
/// Model pola magnetycznego Ziemi WMM2025 (NOAA NCEI / BGS), stopień 12, ważny 2025.0–2030.0.
/// Liczy deklinację magnetyczną i jej roczną zmianę dla punktu i daty — do diagramu GN/MN/TN
/// na wydruku mapy. Współczynniki: plik WMM.COF wydany 13.11.2024 (domena publiczna).
/// </summary>
internal static class GeoMagneticModel
{
    public const string ModelName = "WMM2025";
    public const double Epoch = 2025.0;
    public const double ValidUntil = 2030.0;

    private const int NMax = 12;
    private const double ReferenceRadiusKm = 6371.2;
    private const double WgsSemiMajorKm = 6378.137;
    private const double WgsFlattening = 1.0 / 298.257223563;

    // n  m        g          h        g_dot      h_dot   (nT, nT/rok)
    private const string Coefficients = @"
  1  0  -29351.8       0.0       12.0        0.0
  1  1   -1410.8    4545.4        9.7      -21.5
  2  0   -2556.6       0.0      -11.6        0.0
  2  1    2951.1   -3133.6       -5.2      -27.7
  2  2    1649.3    -815.1       -8.0      -12.1
  3  0    1361.0       0.0       -1.3        0.0
  3  1   -2404.1     -56.6       -4.2        4.0
  3  2    1243.8     237.5        0.4       -0.3
  3  3     453.6    -549.5      -15.6       -4.1
  4  0     895.0       0.0       -1.6        0.0
  4  1     799.5     278.6       -2.4       -1.1
  4  2      55.7    -133.9       -6.0        4.1
  4  3    -281.1     212.0        5.6        1.6
  4  4      12.1    -375.6       -7.0       -4.4
  5  0    -233.2       0.0        0.6        0.0
  5  1     368.9      45.4        1.4       -0.5
  5  2     187.2     220.2        0.0        2.2
  5  3    -138.7    -122.9        0.6        0.4
  5  4    -142.0      43.0        2.2        1.7
  5  5      20.9     106.1        0.9        1.9
  6  0      64.4       0.0       -0.2        0.0
  6  1      63.8     -18.4       -0.4        0.3
  6  2      76.9      16.8        0.9       -1.6
  6  3    -115.7      48.8        1.2       -0.4
  6  4     -40.9     -59.8       -0.9        0.9
  6  5      14.9      10.9        0.3        0.7
  6  6     -60.7      72.7        0.9        0.9
  7  0      79.5       0.0       -0.0        0.0
  7  1     -77.0     -48.9       -0.1        0.6
  7  2      -8.8     -14.4       -0.1        0.5
  7  3      59.3      -1.0        0.5       -0.8
  7  4      15.8      23.4       -0.1        0.0
  7  5       2.5      -7.4       -0.8       -1.0
  7  6     -11.1     -25.1       -0.8        0.6
  7  7      14.2      -2.3        0.8       -0.2
  8  0      23.2       0.0       -0.1        0.0
  8  1      10.8       7.1        0.2       -0.2
  8  2     -17.5     -12.6        0.0        0.5
  8  3       2.0      11.4        0.5       -0.4
  8  4     -21.7      -9.7       -0.1        0.4
  8  5      16.9      12.7        0.3       -0.5
  8  6      15.0       0.7        0.2       -0.6
  8  7     -16.8      -5.2       -0.0        0.3
  8  8       0.9       3.9        0.2        0.2
  9  0       4.6       0.0       -0.0        0.0
  9  1       7.8     -24.8       -0.1       -0.3
  9  2       3.0      12.2        0.1        0.3
  9  3      -0.2       8.3        0.3       -0.3
  9  4      -2.5      -3.3       -0.3        0.3
  9  5     -13.1      -5.2        0.0        0.2
  9  6       2.4       7.2        0.3       -0.1
  9  7       8.6      -0.6       -0.1       -0.2
  9  8      -8.7       0.8        0.1        0.4
  9  9     -12.9      10.0       -0.1        0.1
 10  0      -1.3       0.0        0.1        0.0
 10  1      -6.4       3.3        0.0        0.0
 10  2       0.2       0.0        0.1       -0.0
 10  3       2.0       2.4        0.1       -0.2
 10  4      -1.0       5.3       -0.0        0.1
 10  5      -0.6      -9.1       -0.3       -0.1
 10  6      -0.9       0.4        0.0        0.1
 10  7       1.5      -4.2       -0.1        0.0
 10  8       0.9      -3.8       -0.1       -0.1
 10  9      -2.7       0.9       -0.0        0.2
 10 10      -3.9      -9.1       -0.0       -0.0
 11  0       2.9       0.0        0.0        0.0
 11  1      -1.5       0.0       -0.0       -0.0
 11  2      -2.5       2.9        0.0        0.1
 11  3       2.4      -0.6        0.0       -0.0
 11  4      -0.6       0.2        0.0        0.1
 11  5      -0.1       0.5       -0.1       -0.0
 11  6      -0.6      -0.3        0.0       -0.0
 11  7      -0.1      -1.2       -0.0        0.1
 11  8       1.1      -1.7       -0.1       -0.0
 11  9      -1.0      -2.9       -0.1        0.0
 11 10      -0.2      -1.8       -0.1        0.0
 11 11       2.6      -2.3       -0.1        0.0
 12  0      -2.0       0.0        0.0        0.0
 12  1      -0.2      -1.3        0.0       -0.0
 12  2       0.3       0.7       -0.0        0.0
 12  3       1.2       1.0       -0.0       -0.1
 12  4      -1.3      -1.4       -0.0        0.1
 12  5       0.6      -0.0       -0.0       -0.0
 12  6       0.6       0.6        0.1       -0.0
 12  7       0.5      -0.1       -0.0       -0.0
 12  8      -0.1       0.8        0.0        0.0
 12  9      -0.4       0.1        0.0       -0.0
 12 10      -0.2      -1.0       -0.1       -0.0
 12 11      -1.3       0.1       -0.0        0.0
 12 12      -0.7       0.2       -0.1       -0.1
";

    private static readonly double[,] G = new double[NMax + 1, NMax + 1];
    private static readonly double[,] H = new double[NMax + 1, NMax + 1];
    private static readonly double[,] GDot = new double[NMax + 1, NMax + 1];
    private static readonly double[,] HDot = new double[NMax + 1, NMax + 1];
    private static readonly double[,] Schmidt = new double[NMax + 1, NMax + 1];

    static GeoMagneticModel()
    {
        foreach (string raw in Coefficients.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) continue;
            string[] p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 6) continue;
            int n = int.Parse(p[0], CultureInfo.InvariantCulture);
            int m = int.Parse(p[1], CultureInfo.InvariantCulture);
            if (n < 1 || n > NMax || m < 0 || m > n) continue;
            G[n, m] = double.Parse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture);
            H[n, m] = double.Parse(p[3], NumberStyles.Float, CultureInfo.InvariantCulture);
            GDot[n, m] = double.Parse(p[4], NumberStyles.Float, CultureInfo.InvariantCulture);
            HDot[n, m] = double.Parse(p[5], NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // Współczynniki Schmidta (półnormalizacja) przeliczane na normalizację Gaussa.
        Schmidt[0, 0] = 1.0;
        for (int n = 1; n <= NMax; n++)
        {
            Schmidt[n, 0] = Schmidt[n - 1, 0] * (2.0 * n - 1.0) / n;
            for (int m = 1; m <= n; m++)
                Schmidt[n, m] = Schmidt[n, m - 1] * Math.Sqrt((n - m + 1.0) * (m == 1 ? 2.0 : 1.0) / (n + m));
        }
    }

    /// <summary>Wynik modelu w punkcie: kąty w stopniach (wschód dodatni), natężenia w nT.</summary>
    public readonly record struct Result(double DeclinationDeg, double AnnualChangeDeg, double InclinationDeg, double HorizontalNt, double TotalNt);

    /// <summary>Rok dziesiętny (np. 2026.9) dla daty kalendarzowej.</summary>
    public static double DecimalYear(DateTime date)
    {
        int year = date.Year;
        double daysInYear = DateTime.IsLeapYear(year) ? 366.0 : 365.0;
        return year + (date.DayOfYear - 1 + date.TimeOfDay.TotalDays) / daysInYear;
    }

    /// <summary>True, gdy data mieści się w okresie ważności modelu.</summary>
    public static bool IsValidFor(double decimalYear) => decimalYear >= Epoch && decimalYear < ValidUntil;

    /// <summary>Pole magnetyczne dla szerokości/długości geodezyjnej WGS84 (stopnie), wysokości nad elipsoidą (km) i roku dziesiętnego.</summary>
    public static Result Compute(double latDeg, double lonDeg, double altitudeKm, double decimalYear)
    {
        double dt = decimalYear - Epoch;
        var (x, y, z) = Field(latDeg, lonDeg, altitudeKm, dt, secularVariation: false);
        var (xd, yd, _) = Field(latDeg, lonDeg, altitudeKm, dt, secularVariation: true);

        double h2 = x * x + y * y;
        double h = Math.Sqrt(h2);
        double declination = Math.Atan2(y, x) * 180.0 / Math.PI;
        double inclination = Math.Atan2(z, h) * 180.0 / Math.PI;
        double annual = h2 > 1e-9 ? (x * yd - y * xd) / h2 * 180.0 / Math.PI : 0.0;
        return new Result(declination, annual, inclination, h, Math.Sqrt(h2 + z * z));
    }

    private static (double X, double Y, double Z) Field(double latDeg, double lonDeg, double altitudeKm, double dt, bool secularVariation)
    {
        double phi = latDeg * Math.PI / 180.0;
        double lambda = lonDeg * Math.PI / 180.0;
        double e2 = WgsFlattening * (2.0 - WgsFlattening);

        // Geodezyjne -> geocentryczne sferyczne.
        double sinPhi = Math.Sin(phi), cosPhi = Math.Cos(phi);
        double rc = WgsSemiMajorKm / Math.Sqrt(1.0 - e2 * sinPhi * sinPhi);
        double p = (rc + altitudeKm) * cosPhi;
        double zc = (rc * (1.0 - e2) + altitudeKm) * sinPhi;
        double r = Math.Sqrt(p * p + zc * zc);
        double phiC = Math.Asin(zc / r);

        double theta = Math.PI / 2.0 - phiC;
        double ct = Math.Cos(theta), st = Math.Sin(theta);

        // Funkcje Legendre'a (normalizacja Gaussa) i ich pochodne po kolatitudzie.
        var pnm = new double[NMax + 1, NMax + 1];
        var dpnm = new double[NMax + 1, NMax + 1];
        pnm[0, 0] = 1.0;
        for (int n = 1; n <= NMax; n++)
        {
            for (int m = 0; m <= n; m++)
            {
                if (n == m)
                {
                    pnm[n, m] = st * pnm[n - 1, m - 1];
                    dpnm[n, m] = st * dpnm[n - 1, m - 1] + ct * pnm[n - 1, m - 1];
                }
                else if (n == 1)
                {
                    pnm[n, m] = ct * pnm[0, 0];
                    dpnm[n, m] = ct * dpnm[0, 0] - st * pnm[0, 0];
                }
                else
                {
                    double k = ((n - 1.0) * (n - 1.0) - m * m) / ((2.0 * n - 1.0) * (2.0 * n - 3.0));
                    double p2 = n - 2 >= m ? pnm[n - 2, m] : 0.0;
                    double dp2 = n - 2 >= m ? dpnm[n - 2, m] : 0.0;
                    pnm[n, m] = ct * pnm[n - 1, m] - k * p2;
                    dpnm[n, m] = ct * dpnm[n - 1, m] - st * pnm[n - 1, m] - k * dp2;
                }
            }
        }

        double br = 0.0, bt = 0.0, bp = 0.0;
        for (int n = 1; n <= NMax; n++)
        {
            double ar = Math.Pow(ReferenceRadiusKm / r, n + 2);
            for (int m = 0; m <= n; m++)
            {
                double g = secularVariation ? GDot[n, m] : G[n, m] + dt * GDot[n, m];
                double h = secularVariation ? HDot[n, m] : H[n, m] + dt * HDot[n, m];
                g *= Schmidt[n, m];
                h *= Schmidt[n, m];
                double cm = Math.Cos(m * lambda), sm = Math.Sin(m * lambda);
                br += (n + 1) * ar * (g * cm + h * sm) * pnm[n, m];
                bt -= ar * (g * cm + h * sm) * dpnm[n, m];
                if (st > 1e-10)
                    bp += ar * m * (g * sm - h * cm) * pnm[n, m] / st;
            }
        }

        // Składowe sferyczne -> geodezyjne (X północ, Y wschód, Z w dół).
        double xs = -bt, ys = bp, zs = -br;
        double d = phiC - phi;
        double x = xs * Math.Cos(d) - zs * Math.Sin(d);
        double z = xs * Math.Sin(d) + zs * Math.Cos(d);
        return (x, ys, z);
    }
}
