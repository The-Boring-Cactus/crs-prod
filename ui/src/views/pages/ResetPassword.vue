<script setup>
import { ref, computed } from 'vue';
import { useRoute, useRouter } from 'vue-router';
import { toast } from 'vue-sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { BarChart, Eye, EyeOff } from 'lucide-vue-next';
import { APP_NAME } from '@/config/brand';

const route = useRoute();
const router = useRouter();

const token = computed(() => route.query.token || '');
const newPassword = ref('');
const confirmPassword = ref('');
const submitting = ref(false);
const showPw = ref(false);
const showConfirmPw = ref(false);
const done = ref(false);

const canSubmit = computed(() =>
    !!token.value && newPassword.value.length >= 6 && newPassword.value === confirmPassword.value
);

async function submit() {
    if (!token.value) {
        toast.error('Invalid link', { description: 'This reset link is missing its token.' });
        return;
    }
    if (newPassword.value.length < 6) {
        toast.error('Password too short', { description: 'Use at least 6 characters.' });
        return;
    }
    if (newPassword.value !== confirmPassword.value) {
        toast.error('Passwords do not match');
        return;
    }

    submitting.value = true;
    try {
        const resp = await fetch(`${import.meta.env.VITE_API_URL}/api/auth/reset-password`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ token: token.value, newPassword: newPassword.value })
        });
        const data = await resp.json();
        if (data.success) {
            done.value = true;
            toast.success('Password reset', { description: data.message });
            setTimeout(() => router.push('/auth/login'), 2000);
        } else {
            toast.error('Could not reset password', { description: data.message || 'Please request a new reset link.' });
        }
    } catch (error) {
        toast.error('Something went wrong', { description: error.message });
    } finally {
        submitting.value = false;
    }
}
</script>

<template>
    <div class="login-card">
        <div class="text-center mb-4">
            <BarChart class="mx-auto" style="width: 3rem; height: 3rem; color: #8b5cf6" />
            <h2 class="mt-3 text-2xl font-bold">{{ APP_NAME }}</h2>
            <p class="text-muted-foreground">Choose a new password</p>
        </div>

        <div v-if="!token" class="text-center text-sm text-destructive mt-4">
            This link is missing its reset token. Please request a new one from the login screen.
        </div>

        <template v-else-if="!done">
            <div class="grid w-full items-center gap-1.5 mt-4">
                <Label for="new-password">New Password</Label>
                <div class="relative">
                    <Input id="new-password" v-model="newPassword" :type="showPw ? 'text' : 'password'" class="pr-10" @keyup.enter="submit" />
                    <button type="button" class="absolute right-3 top-2.5 text-muted-foreground" @click="showPw = !showPw">
                        <EyeOff v-if="showPw" class="w-4 h-4" /><Eye v-else class="w-4 h-4" />
                    </button>
                </div>
            </div>

            <div class="grid w-full items-center gap-1.5 mt-4">
                <Label for="confirm-password">Confirm Password</Label>
                <div class="relative">
                    <Input id="confirm-password" v-model="confirmPassword" :type="showConfirmPw ? 'text' : 'password'" class="pr-10" @keyup.enter="submit" />
                    <button type="button" class="absolute right-3 top-2.5 text-muted-foreground" @click="showConfirmPw = !showConfirmPw">
                        <EyeOff v-if="showConfirmPw" class="w-4 h-4" /><Eye v-else class="w-4 h-4" />
                    </button>
                </div>
                <p v-if="confirmPassword && newPassword !== confirmPassword" class="text-xs text-destructive">Passwords do not match</p>
            </div>

            <Button class="w-full mt-6" @click="submit" :disabled="!canSubmit || submitting">
                <span v-if="submitting" class="animate-spin mr-2">⠋</span>
                Reset Password
            </Button>
        </template>

        <div v-else class="text-center text-sm text-muted-foreground mt-4">
            Redirecting you to login...
        </div>

        <div class="text-center mt-4">
            <a href="/auth/login" class="text-primary hover:underline text-sm" @click.prevent="router.push('/auth/login')">Back to login</a>
        </div>
    </div>
</template>
