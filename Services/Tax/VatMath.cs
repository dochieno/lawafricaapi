namespace LawAfrica.API.Services.Tax
{
    public static class VatMath
    {
        public static (decimal Net, decimal Vat, decimal Gross) FromNet(decimal net, decimal ratePercent)
        {
            var vat = Round2(net * (ratePercent / 100m));
            var gross = Round2(net + vat);
            return (Round2(net), vat, gross);
        }

        public static (decimal Net, decimal Vat, decimal Gross) FromGrossInclusive(decimal gross, decimal ratePercent)
        {
            if (ratePercent <= 0) return (Round2(gross), 0m, Round2(gross));

            var divisor = 1m + (ratePercent / 100m);
            var net = Round2(gross / divisor);
            var vat = Round2(gross - net);
            return (net, vat, Round2(gross));
        }

        public static decimal Round2(decimal v) =>
            Math.Round(v, 2, MidpointRounding.AwayFromZero);
    }
}
