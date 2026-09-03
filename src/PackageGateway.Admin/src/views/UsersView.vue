<template>
  <div class="page-container">
    <IdentityManagement :api="identityApi" :format-date-time="formatDateTime" :can-delete-provider="provider => provider.type !== 'entra'" />
  </div>
</template>

<script setup lang="ts">
import type { IdentityApi } from '@aditify/identity';
import { createGraphqlIdentityCompatibilityApi, createIdentityApi, IdentityManagement } from '@aditify/identity';
import { graphql } from '@/api/graphql';
import { formatDateTime } from '@/utils/dateTime';

const legacy = createGraphqlIdentityCompatibilityApi({ graphql, lastLoginField: 'lastLoginAt', mutationPayloads: true });
const external = createIdentityApi();
const identityApi: IdentityApi = {
  ...external,
  providers: async () => [...await external.providers(), ...await legacy.providers()],
  saveProvider: (provider, secret) => provider.type === 'entra' ? legacy.saveProvider(provider, secret) : external.saveProvider(provider, secret),
  deleteProvider: id => external.deleteProvider(id),
  testProvider: (provider, secret) => provider.type === 'entra' ? legacy.testProvider(provider, secret) : external.testProvider(provider, secret),
};
</script>
