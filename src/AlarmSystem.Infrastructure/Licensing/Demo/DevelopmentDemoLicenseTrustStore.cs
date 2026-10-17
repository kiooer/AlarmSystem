namespace AlarmSystem.Infrastructure.Licensing.Demo;

public static class DevelopmentDemoLicenseTrustStore
{
    public static IDemoLicenseTrustStore Create(bool isDevelopment)
    {
#if DEBUG
        if (isDevelopment)
        {
            return new DemoLicenseTrustStore(new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["dev-ui-20261017"] = Convert.FromBase64String(
                    "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEjptDpL1E/aI2B8EP21h7y2gMv2yHO9h79BLwKfTwGA7fbTZpohMxxJACC/uNJVC7iIuDO2XZKzDUh5rv/gmDuw=="),
            });
        }
#endif

        return new DemoLicenseTrustStore();
    }
}
