// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Azure.Common;
using Aspire.Azure.Security.KeyVault;
using Azure.Core;
using Azure.Core.Extensions;
using Azure.Security.KeyVault.Secrets;
using HealthChecks.Azure.KeyVault.Secrets;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Extensions.Hosting;

public static class AspireKeyVaultExtensions
{
    internal const string DefaultConfigSectionName = "Aspire:Azure:Security:KeyVault";

    /// <summary>
    /// Registers <see cref="SecretClient"/> as a singleton in the services provided by the <paramref name="builder"/>.
    /// Enables retries, corresponding health check, logging and telemetry.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="AzureSecurityKeyVaultSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureClientBuilder">An optional method that can be used for customizing the <see cref="IAzureClientBuilder{SecretClient, SecretClientOptions}"/>.</param>
    /// <remarks>Reads the configuration from "Aspire.Azure.Security.KeyVault" section.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when mandatory <see cref="AzureSecurityKeyVaultSettings.VaultUri"/> is not provided.</exception>
    public static void AddAzureKeyVaultSecrets(
        this IHostApplicationBuilder builder,
        Action<AzureSecurityKeyVaultSettings>? configureSettings = null,
        Action<IAzureClientBuilder<SecretClient, SecretClientOptions>>? configureClientBuilder = null)
    {
        new KeyVaultComponent().AddClient(builder, DefaultConfigSectionName, configureSettings, configureClientBuilder, name: null);
    }

    /// <summary>
    /// Registers <see cref="SecretClient"/> as a singleton for given <paramref name="name"/> in the services provided by the <paramref name="builder"/>.
    /// Enables retries, corresponding health check, logging and telemetry.
    /// </summary>
    /// <param name="builder">The <see cref="IHostApplicationBuilder" /> to read config from and add services to.</param>
    /// <param name="name">The <see cref="ServiceDescriptor.ServiceKey"/> of the service.</param>
    /// <param name="configureSettings">An optional method that can be used for customizing the <see cref="AzureSecurityKeyVaultSettings"/>. It's invoked after the settings are read from the configuration.</param>
    /// <param name="configureClientBuilder">An optional method that can be used for customizing the <see cref="IAzureClientBuilder{SecretClient, SecretClientOptions}"/>.</param>
    /// <remarks>Reads the configuration from "Aspire.Azure.Security.KeyVault:{name}" section.</remarks>
    /// <exception cref="InvalidOperationException">Thrown when mandatory <see cref="AzureSecurityKeyVaultSettings.VaultUri"/> is not provided.</exception>
    public static void AddAzureKeyVaultSecrets(
        this IHostApplicationBuilder builder,
        string name,
        Action<AzureSecurityKeyVaultSettings>? configureSettings = null,
        Action<IAzureClientBuilder<SecretClient, SecretClientOptions>>? configureClientBuilder = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        string configurationSectionName = KeyVaultComponent.GetKeyedConfigurationSectionName(name, DefaultConfigSectionName);

        new KeyVaultComponent().AddClient(builder, configurationSectionName, configureSettings, configureClientBuilder, name);
    }

    private sealed class KeyVaultComponent : AzureComponent<AzureSecurityKeyVaultSettings, SecretClient, SecretClientOptions>
    {
        protected override IAzureClientBuilder<SecretClient, SecretClientOptions> AddClient<TBuilder>(TBuilder azureFactoryBuilder, AzureSecurityKeyVaultSettings settings)
            => azureFactoryBuilder.AddSecretClient(settings.VaultUri);

        protected override IHealthCheck CreateHealthCheck(SecretClient client, AzureSecurityKeyVaultSettings settings)
            => new AzureKeyVaultSecretsHealthCheck(client, new AzureKeyVaultSecretOptions());

        protected override bool GetHealthCheckEnabled(AzureSecurityKeyVaultSettings settings)
            => settings.HealthChecks;

        protected override TokenCredential? GetTokenCredential(AzureSecurityKeyVaultSettings settings)
            => settings.Credential;

        protected override bool GetTracingEnabled(AzureSecurityKeyVaultSettings settings)
            => settings.Tracing;

        protected override void Validate(AzureSecurityKeyVaultSettings settings, string configurationSectionName)
        {
            if (settings.VaultUri is null)
            {
                throw new InvalidOperationException($"VaultUri is missing. It should be provided under 'VaultUri' key in '{configurationSectionName}' configuration section.");
            }
        }
    }
}