<template>
  <AlertDialog :open="modelValue" @update:open="emit('update:modelValue', $event)">
    <AlertDialogContent @escape-key-down="loading && $event.preventDefault()" @pointer-down-outside="loading && $event.preventDefault()">
      <AlertDialogHeader>
        <AlertDialogTitle>{{ title }}</AlertDialogTitle>
        <AlertDialogDescription>{{ message }}</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel :disabled="loading" @click="emit('update:modelValue', false)">
          {{ cancelText }}
        </AlertDialogCancel>
        <AlertDialogAction :variant="confirmColor === 'error' ? 'destructive' : 'default'" :disabled="loading" @click="emit('confirm')">
          <Spinner v-if="loading" />{{ confirmText }}
        </AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>

<script setup lang="ts">
import { AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader, AlertDialogTitle, Spinner } from '@aditify/ui';

withDefaults(defineProps<{
  modelValue: boolean;
  title: string;
  message: string;
  confirmText: string;
  cancelText: string;
  confirmColor?: string;
  loading?: boolean;
}>(), { confirmColor: 'error', loading: false });

const emit = defineEmits<{
  (event: 'update:modelValue', value: boolean): void;
  (event: 'confirm'): void;
}>();
</script>
