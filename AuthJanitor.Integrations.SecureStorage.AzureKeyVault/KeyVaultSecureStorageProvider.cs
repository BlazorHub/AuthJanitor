﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
using AuthJanitor.Extensions.Azure;
using AuthJanitor.Integrations.CryptographicImplementations;
using AuthJanitor.Integrations.IdentityServices;
using Azure.Security.KeyVault.Secrets;
using Newtonsoft.Json;
using System;
using System.Threading.Tasks;

namespace AuthJanitor.Integrations.SecureStorage.AzureKeyVault
{
    public class KeyVaultSecureStorageProvider : ISecureStorage
    {
        private const string PERSISTENCE_PREFIX = "AJPersist-";
        private readonly string _vaultName;

        private readonly IIdentityService _identityService;
        private readonly ICryptographicImplementation _cryptographicImplementation;

        public KeyVaultSecureStorageProvider(
            AuthJanitorServiceConfiguration serviceConfiguration,
            IIdentityService identityService,
            ICryptographicImplementation cryptographicImplementation)
        {
            _identityService = identityService;
            _cryptographicImplementation = cryptographicImplementation;

            _vaultName = serviceConfiguration.SecurePersistenceContainerName;

            if (_cryptographicImplementation == null)
                throw new InvalidOperationException("ICryptographicImplementation must be registered!");
        }

        public async Task Destroy(Guid persistenceId)
        {
            var client = await GetClient();
            await client.StartDeleteSecretAsync($"{PERSISTENCE_PREFIX}{persistenceId}");
        }

        public async Task<Guid> Persist<T>(DateTimeOffset expiry, T persistedObject)
        {
            var newId = Guid.NewGuid();
            var newSecret = new KeyVaultSecret($"{PERSISTENCE_PREFIX}{newId}",
                await _cryptographicImplementation.Encrypt(newId.ToString(), JsonConvert.SerializeObject(persistedObject)));
            newSecret.Properties.ExpiresOn = expiry;

            var client = await GetClient();
            await client.SetSecretAsync(newSecret);
            return newId;
        }

        public async Task<T> Retrieve<T>(Guid persistenceId)
        {
            var client = await GetClient();
            var secret = await client.GetSecretAsync($"{PERSISTENCE_PREFIX}{persistenceId}");
            if (secret == null || secret.Value == null)
                throw new Exception("Secret not found");

            return JsonConvert.DeserializeObject<T>(await _cryptographicImplementation.Decrypt(persistenceId.ToString(), secret.Value.Value));
        }

        private Task<SecretClient> GetClient() =>
            _identityService.GetAccessTokenForApplicationAsync()
                .ContinueWith(t => t.Result.CreateTokenCredential())
                .ContinueWith(t => new SecretClient(
                    new Uri($"https://{_vaultName}.vault.azure.net/"),
                    t.Result));
    }
}