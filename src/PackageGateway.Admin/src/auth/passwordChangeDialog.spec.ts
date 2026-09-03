import { describe, expect, it } from 'vitest';
import { ref } from 'vue';
import { usePasswordChangeDialog } from './passwordChangeDialog';

describe('password change dialog', () => {
  it('keeps a validation message visible when a persistent dialog rejects a close event', () => {
    const mustChangePassword = ref(true);
    const dialog = usePasswordChangeDialog(mustChangePassword);

    dialog.passwordChangeError.value = 'The current password is incorrect.';
    dialog.updatePasswordDialog(false);

    expect(dialog.passwordDialog.value).toBe(true);
    expect(dialog.passwordChangeError.value).toBe('The current password is incorrect.');
  });

  it('allows the optional password dialog to close', () => {
    const mustChangePassword = ref(false);
    const dialog = usePasswordChangeDialog(mustChangePassword);

    dialog.openPasswordDialog();
    dialog.updatePasswordDialog(false);

    expect(dialog.passwordDialog.value).toBe(false);
  });

  it('closes the required dialog when the authentication session is cleared', async () => {
    const mustChangePassword = ref(true);
    const dialog = usePasswordChangeDialog(mustChangePassword);

    dialog.passwordChangeError.value = 'Forbidden';

    mustChangePassword.value = false;
    await Promise.resolve();

    expect(dialog.passwordDialog.value).toBe(false);
    expect(dialog.passwordChangeError.value).toBe('');
  });
});
