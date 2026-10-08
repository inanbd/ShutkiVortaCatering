using System.Data;
using System.Globalization;
using Dapper;

namespace ShutkiVorta.Infrastructure.Persistence;

/// <summary>Global Dapper settings shared by the SQLite and SQL Server providers.</summary>
internal static class DapperConfiguration
{
    private static int _configured;

    public static void Configure()
    {
        if (Interlocked.Exchange(ref _configured, 1) == 1)
        {
            return;
        }

        // SQLite has no decimal type: NUMERIC columns come back as Int64 for whole numbers (2 lb) and
        // Double for fractions (1.5 lb), even within one result set. This handler reads either safely.
        SqlMapper.AddTypeHandler(new FlexibleDecimalHandler());
    }

    private sealed class FlexibleDecimalHandler : SqlMapper.TypeHandler<decimal>
    {
        public override decimal Parse(object value) => value switch
        {
            decimal d => d,
            long l => l,
            int i => i,
            double d => Convert.ToDecimal(d), // rounds to 15 significant digits, ideal for money and pounds
            float f => Convert.ToDecimal(f),
            string s => decimal.Parse(s, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture),
            _ => Convert.ToDecimal(value, CultureInfo.InvariantCulture),
        };

        public override void SetValue(IDbDataParameter parameter, decimal value)
        {
            parameter.DbType = DbType.Decimal;
            parameter.Value = value;
        }
    }
}
