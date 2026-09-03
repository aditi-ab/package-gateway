import type { Ref } from 'vue';
import { ref, watch } from 'vue';

export function usePasswordChangeDialog(mustChangePassword: Ref<boolean>) {
  const passwordDialog = ref(false);
  const currentLocalPassword = ref('');
  const newLocalPassword = ref('');
  const passwordChangeError = ref('');
  const passwordChangeBusy = ref(false);

  watch(mustChangePassword, (value) => {
    if (value) {
      passwordChangeError.value = '';
      passwordDialog.value = true;
    }
    else {
      passwordDialog.value = false;
      resetPasswordDialog();
    }
  }, { immediate: true });

  function openPasswordDialog() {
    passwordChangeError.value = '';
    passwordDialog.value = true;
  }

  function updatePasswordDialog(value: boolean) {
    if (!value && mustChangePassword.value)
      return;

    passwordDialog.value = value;
  }

  function resetPasswordDialog() {
    passwordChangeError.value = '';
    currentLocalPassword.value = '';
    newLocalPassword.value = '';
  }

  return {
    currentLocalPassword,
    newLocalPassword,
    openPasswordDialog,
    passwordChangeBusy,
    passwordChangeError,
    passwordDialog,
    resetPasswordDialog,
    updatePasswordDialog,
  };
}
