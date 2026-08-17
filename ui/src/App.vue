<script setup>
import { ref, computed, getCurrentInstance, onMounted, watch } from 'vue';
import { useRouter, useRoute } from 'vue-router';
import { userStoreMe } from '@/store/userStore';
import { useProjectStore } from '@/store/projectStore';
import { toast } from 'vue-sonner';
import { Toaster } from '@/components/ui/sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import { BarChart, Home, Database, FileSpreadsheet, Code, FileCode2, LogOut, Search, Settings, Bell, AlignRight, FileText, ChevronRight, ChevronDown, Pencil, Plus, Trash2, Sun, Moon, Palette, Check, Eye, EyeOff, X, Network, UserPlus, Loader2 } from 'lucide-vue-next';
import { useLayout } from '@/layout/composables/layout';
import { APP_NAME } from '@/config/brand';

const router = useRouter();
const route = useRoute();
const userStore = userStoreMe();
const projectStore = useProjectStore();
const { proxy } = getCurrentInstance();
const { layoutConfig, toggleDarkMode, setThemeColor, themeColors } = useLayout();

const isSetupRoute = computed(() => route.path === '/setup');
const isPublicRoute = computed(() => route.path.startsWith('/share/') || route.path.startsWith('/public/'));
const isResetPasswordRoute = computed(() => route.path === '/auth/reset-password');

const loading = ref(false);
const loginData = ref({
    username: '',
    password: ''
});
const searchQuery = ref('');
const isSidebarHovered = ref(false);
const showRegister = ref(false);
const registerData = ref({ username: '', email: '', password: '' });
const showForgotPassword = ref(false);
const forgotPasswordInput = ref('');
const sendingResetEmail = ref(false);
const showProjectDialog = ref(false);
const isEditingProject = ref(false);
const editingProject = ref({ id: undefined, name: '', description: '' });

// Settings dialog
const showSettings = ref(false);
const settingsTab = ref('profile');
const settingsTabs = computed(() => {
    const tabs = [{ id: 'profile', label: 'Profile' }, { id: 'password', label: 'Password' }];
    if (userStore.isAdmin) tabs.push({ id: 'smtp', label: 'Email' }, { id: 'users', label: 'Users' }, { id: 'audit', label: 'Audit Log' });
    tabs.push({ id: 'appearance', label: 'Appearance' });
    return tabs;
});
const settingsDisplayName = ref('');
const settingsOldPassword = ref('');
const settingsNewPassword = ref('');
const settingsConfirmPassword = ref('');
const savingProfile = ref(false);
const savingPassword = ref(false);
const showOldPw = ref(false);
const showNewPw = ref(false);
const showConfirmPw = ref(false);

// Email (SMTP) settings tab — system-wide, not per-user (see UpdateSmtpConfig
// on the server). The stored password is never sent back; leaving the
// Password field blank on save means "keep the current one".
const smtpLoaded = ref(false);
const smtpLoading = ref(false);
const smtpSaving = ref(false);
const smtpHasPassword = ref(false);
const smtpConfig = ref({
    host: '',
    port: 587,
    fromAddress: '',
    username: '',
    password: '',
    useSsl: true
});

const accentColors = [
    { name: 'indigo', label: 'Indigo', cls: 'bg-indigo-500' },
    { name: 'emerald', label: 'Emerald', cls: 'bg-emerald-500' },
    { name: 'blue', label: 'Blue', cls: 'bg-blue-500' },
    { name: 'rose', label: 'Rose', cls: 'bg-rose-500' },
    { name: 'amber', label: 'Amber', cls: 'bg-amber-500' },
];

const openSettings = () => {
    settingsDisplayName.value = '';
    settingsOldPassword.value = '';
    settingsNewPassword.value = '';
    settingsConfirmPassword.value = '';
    settingsTab.value = 'profile';
    showSettings.value = true;
};

const saveProfile = async () => {
    if (!settingsDisplayName.value.trim()) return;
    savingProfile.value = true;
    try {
        await userStore.executeCommand('UpdateUserProfile', { displayName: settingsDisplayName.value.trim() }, proxy.$socket);
        toast.success('Display name updated');
        settingsDisplayName.value = '';
    } catch (e) {
        toast.error('Failed to update profile', { description: e.message });
    } finally {
        savingProfile.value = false;
    }
};

const changePassword = async () => {
    if (!settingsOldPassword.value || !settingsNewPassword.value) {
        toast.error('Fill in all password fields');
        return;
    }
    if (settingsNewPassword.value !== settingsConfirmPassword.value) {
        toast.error('New passwords do not match');
        return;
    }
    if (settingsNewPassword.value.length < 6) {
        toast.error('New password must be at least 6 characters');
        return;
    }
    savingPassword.value = true;
    try {
        await userStore.executeCommand('ChangePassword', { oldPassword: settingsOldPassword.value, newPassword: settingsNewPassword.value }, proxy.$socket);
        toast.success('Password changed successfully');
        settingsOldPassword.value = '';
        settingsNewPassword.value = '';
        settingsConfirmPassword.value = '';
    } catch (e) {
        toast.error('Failed to change password', { description: e.message });
    } finally {
        savingPassword.value = false;
    }
};

const loadSmtpConfig = async () => {
    smtpLoading.value = true;
    try {
        const result = await userStore.executeCommand('GetSmtpConfig', {}, proxy.$socket);
        const data = result?.Data || result?.data || {};
        smtpConfig.value = {
            host: data.host || '',
            port: data.port || 587,
            fromAddress: data.fromAddress || '',
            username: data.username || '',
            password: '',
            useSsl: data.useSsl !== false
        };
        smtpHasPassword.value = !!data.hasPassword;
        smtpLoaded.value = true;
    } catch (e) {
        toast.error('Failed to load email settings', { description: e.message });
    } finally {
        smtpLoading.value = false;
    }
};

const saveSmtpConfig = async () => {
    if (!smtpConfig.value.host.trim() || !smtpConfig.value.fromAddress.trim()) {
        toast.error('Host and From Address are required');
        return;
    }
    smtpSaving.value = true;
    try {
        await userStore.executeCommand('UpdateSmtpConfig', {
            host: smtpConfig.value.host.trim(),
            port: smtpConfig.value.port,
            fromAddress: smtpConfig.value.fromAddress.trim(),
            username: smtpConfig.value.username.trim(),
            password: smtpConfig.value.password, // blank = leave unchanged (server-side)
            useSsl: smtpConfig.value.useSsl
        }, proxy.$socket);
        toast.success('Email settings saved');
        smtpHasPassword.value = smtpHasPassword.value || !!smtpConfig.value.password;
        smtpConfig.value.password = '';
    } catch (e) {
        toast.error('Failed to save email settings', { description: e.message });
    } finally {
        smtpSaving.value = false;
    }
};

const usersList = ref([]);
const usersLoading = ref(false);
const userRoleUpdating = ref('');

const loadUsers = async () => {
    usersLoading.value = true;
    try {
        const result = await userStore.executeCommand('ListUsers', {}, proxy.$socket);
        usersList.value = result?.Data || result?.data || [];
    } catch (e) {
        toast.error('Failed to load users', { description: e.message });
    } finally {
        usersLoading.value = false;
    }
};

const changeUserRole = async (user, newRole) => {
    if (user.roles === newRole) return;
    userRoleUpdating.value = user.userId;
    try {
        await userStore.executeCommand('UpdateUserRole', { userId: user.userId, role: newRole }, proxy.$socket);
        user.roles = newRole;
        toast.success(`${user.username} is now ${newRole}`);
    } catch (e) {
        toast.error('Failed to update role', { description: e.message });
    } finally {
        userRoleUpdating.value = '';
    }
};

const toggleUserActive = async (user) => {
    const nextActive = !user.isActive;
    try {
        await userStore.executeCommand('SetUserActive', { userId: user.userId, isActive: nextActive }, proxy.$socket);
        user.isActive = nextActive;
        toast.success(nextActive ? `${user.username} activated` : `${user.username} deactivated`);
    } catch (e) {
        toast.error('Failed to update user', { description: e.message });
    }
};

watch(settingsTab, (tab) => {
    if (tab === 'smtp' && !smtpLoading.value) loadSmtpConfig();
    if (tab === 'users' && !usersLoading.value) loadUsers();
    if (tab === 'audit' && !auditLoading.value && !auditLogEntries.value.length) loadAuditLog();
});

// Admin Audit Log: recent create/edit/delete/share activity across the instance, loaded a
// page at a time (server does no DB-side pagination, but the UI still fetches in chunks so
// the admin isn't stuck waiting on a giant single load as the log grows).
const AUDIT_PAGE_SIZE = 50;
const auditLogEntries = ref([]);
const auditLoading = ref(false);
const auditHasMore = ref(false);

const loadAuditLog = async (loadMore = false) => {
    auditLoading.value = true;
    try {
        const offset = loadMore ? auditLogEntries.value.length : 0;
        const result = await userStore.executeCommand('ListAuditLog', { limit: AUDIT_PAGE_SIZE, offset }, proxy.$socket);
        const rows = result?.Data || [];
        auditLogEntries.value = loadMore ? [...auditLogEntries.value, ...rows] : rows;
        auditHasMore.value = rows.length === AUDIT_PAGE_SIZE;
    } catch (e) {
        toast.error('Failed to load audit log', { description: e.message });
    } finally {
        auditLoading.value = false;
    }
};

// Check if user is authenticated
const isLoggedIn = computed(() => userStore.auth);
const userName = computed(() => userStore.name || 'User');
const userInitial = computed(() => userName.value.charAt(0).toUpperCase());

const login = async () => {
    loading.value = true;
    try {
        await userStore.authenticate(loginData.value.username, loginData.value.password, proxy.$socket);
        await projectStore.loadProjects(proxy.$socket);
        router.push('/');
    } catch (error) {
        toast.error('Authentication Failed', { description: 'Please check your credentials.' });
    } finally {
        loading.value = false;
    }
};

const logout = () => {
    localStorage.removeItem('crs_token');
    try { proxy.$socket?.close(); } catch (_) {}
    userStore.setCurr(false, '', '', []);
    projectStore.setCurrentProject(null);
    router.push('/');
};

onMounted(async () => {
    if (!userStore.auth) {
        try {
            await userStore.restoreSession(proxy.$socket);
            await projectStore.loadProjects(proxy.$socket);
            router.push('/');
        } catch (_) {
            // No stored session — show login form
        }
    } else {
        await projectStore.loadProjects(proxy.$socket);
    }
});

const handleRegister = async () => {
    if (!registerData.value.username || !registerData.value.password) {
        toast.error('Error', { description: 'Username and password are required' });
        return;
    }
    loading.value = true;
    try {
        const resp = await fetch(`${import.meta.env.VITE_API_URL}/api/auth/register`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                username: registerData.value.username,
                email: registerData.value.email,
                password: registerData.value.password
            })
        });
        if (!resp.ok) {
            const err = await resp.json();
            throw new Error(err.message || 'Registration failed');
        }
        toast.success('Account created', { description: 'You can now log in.' });
        showRegister.value = false;
        registerData.value = { username: '', email: '', password: '' };
    } catch (error) {
        toast.error('Registration failed', { description: error.message });
    } finally {
        loading.value = false;
    }
};

const handleForgotPassword = async () => {
    if (!forgotPasswordInput.value.trim()) {
        toast.error('Error', { description: 'Enter your username or email' });
        return;
    }
    sendingResetEmail.value = true;
    try {
        const resp = await fetch(`${import.meta.env.VITE_API_URL}/api/auth/forgot-password`, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                usernameOrEmail: forgotPasswordInput.value.trim(),
                origin: window.location.origin
            })
        });
        const data = await resp.json();
        // Always shows the same generic message regardless of outcome — the
        // backend intentionally never reveals whether the account exists.
        toast.success('Check your email', { description: data.message || 'If an account exists, a reset link has been sent.' });
        showForgotPassword.value = false;
        forgotPasswordInput.value = '';
    } catch (error) {
        toast.error('Something went wrong', { description: error.message });
    } finally {
        sendingResetEmail.value = false;
    }
};

const navigateTo = (path) => {
    router.push(path);
};

const isActive = (path) => {
    return route.path === path;
};

const projectSubItems = [
    { icon: Database, key: 'databases', label: 'Databases', path: '/pages/databases' },
    { icon: Network, key: 'datamodels', label: 'Data Models', path: '/pages/datamodels' },
    { icon: Code, key: 'sqleditor', label: 'SQL Queries', path: '/pages/sqleditor' },
    { icon: FileCode2, key: 'cseditor', label: 'Scripts', path: '/pages/cseditor' },
    { icon: FileSpreadsheet, key: 'myexcel', label: 'Datasets', path: '/pages/myexcel' },
    { icon: BarChart, key: 'dashboard', label: 'Dashboards', path: '/pages/dashboard' },
];

const selectProjectItem = (project, subItem) => {
    projectStore.setCurrentProject(project.id);
    router.push(subItem.path);
};

const openNewProject = () => {
    isEditingProject.value = false;
    editingProject.value = { id: undefined, name: '', description: '' };
    projectMembers.value = [];
    showProjectDialog.value = true;
};

const openEditProject = (project) => {
    isEditingProject.value = true;
    editingProject.value = { id: project.id, name: project.name, description: project.description };
    showProjectDialog.value = true;
    loadProjectMembers();
};

// Project membership: grants a teammate view/edit access to everything in this project
// (its dashboards, scripts, data models, and non-global database connections). Only the
// project's owner (or an admin) can manage this -- enforced server-side.
const projectMembers = ref([]);
const loadingProjectMembers = ref(false);
const memberQuery = ref('');
const memberPermission = ref('view');
const addingMember = ref(false);

const loadProjectMembers = async () => {
    if (!editingProject.value.id) { projectMembers.value = []; return; }
    loadingProjectMembers.value = true;
    try {
        const result = await userStore.executeCommand('ListResourceGrants',
            { resourceType: 'Projects', resourceId: editingProject.value.id }, proxy.$socket);
        projectMembers.value = result?.Data || [];
    } catch {
        projectMembers.value = [];
    } finally {
        loadingProjectMembers.value = false;
    }
};

const addProjectMember = async () => {
    if (!memberQuery.value.trim() || !editingProject.value.id) return;
    addingMember.value = true;
    try {
        await userStore.executeCommand('ShareResource', {
            resourceType: 'Projects', resourceId: editingProject.value.id,
            grantee: memberQuery.value.trim(), permission: memberPermission.value
        }, proxy.$socket);
        memberQuery.value = '';
        await loadProjectMembers();
        toast.success('Member added');
    } catch (error) {
        toast.error('Failed to add member', { description: error.message });
    } finally {
        addingMember.value = false;
    }
};

const removeProjectMember = async (granteeUserId) => {
    try {
        await userStore.executeCommand('RevokeResourceGrant',
            { resourceType: 'Projects', resourceId: editingProject.value.id, granteeUserId }, proxy.$socket);
        await loadProjectMembers();
        toast.success('Member removed');
    } catch (error) {
        toast.error('Failed to remove member', { description: error.message });
    }
};

const saveProject = async () => {
    if (!editingProject.value.name.trim()) {
        toast.error('Name required', { description: 'Please enter a project name.' });
        return;
    }
    try {
        await projectStore.saveProject(editingProject.value, proxy.$socket);
        toast.success(isEditingProject.value ? 'Project updated' : 'Project created');
        showProjectDialog.value = false;
    } catch (error) {
        toast.error('Failed to save project', { description: error.message });
    }
};

const deleteProject = async (id) => {
    try {
        await projectStore.deleteProject(id, proxy.$socket);
        toast.success('Project deleted');
        showProjectDialog.value = false;
    } catch (error) {
        toast.error('Failed to delete project', { description: error.message });
    }
};
</script>

<template>
    <Toaster position="top-right" />

    <!-- Setup Wizard (full screen, no chrome) -->
    <div v-if="isSetupRoute" class="setup-fullscreen">
        <RouterView />
    </div>

    <!-- Public shared views — no auth required -->
    <div v-else-if="isPublicRoute">
        <RouterView />
    </div>

    <!-- Reset Password (reached from an emailed link, no auth required) -->
    <div v-else-if="isResetPasswordRoute" class="login-container">
        <RouterView />
    </div>

    <!-- Login Screen -->
    <div v-else-if="!isLoggedIn" class="login-container">
        <div class="login-card">
            <div class="text-center mb-4">
                <BarChart class="mx-auto" style="width: 3rem; height: 3rem; color: #8b5cf6" />
                <h2 class="mt-3 text-2xl font-bold">{{ APP_NAME }}</h2>
                <p class="text-muted-foreground">Bienvenido de vuelta</p>
            </div>

            <div class="grid w-full items-center gap-1.5 mt-4">
                <Label for="username">Usuario</Label>
                <Input id="username" v-model="loginData.username" type="text" />
            </div>

            <div class="grid w-full items-center gap-1.5 mt-4">
                <Label for="password">Contraseña</Label>
                <Input id="password" v-model="loginData.password" type="password" @keyup.enter="login" />
            </div>

            <Button class="w-full mt-6" @click="login" :disabled="loading">
                <span v-if="loading" class="animate-spin mr-2">⠋</span>
                Iniciar Sesión
            </Button>

            <div class="text-center mt-3">
                <a href="#" style="color: #8b5cf6; text-decoration: none; font-size: 0.9rem" @click.prevent="showForgotPassword = true"> ¿Olvidaste tu contraseña? </a>
            </div>

            <div class="text-center mt-3 text-sm text-muted-foreground">
                Don't have an account?
                <a href="/auth/register" class="text-primary hover:underline cursor-pointer" @click.prevent="showRegister = true">Register</a>
            </div>
        </div>

        <!-- Register Dialog -->
        <div v-if="showRegister" class="fixed inset-0 bg-black/50 flex items-center justify-center z-50" @click.self="showRegister = false">
            <div class="bg-card rounded-2xl p-8 w-full max-w-sm shadow-xl">
                <h3 class="text-xl font-bold mb-6 text-center">Create Account</h3>
                <div class="grid gap-4">
                    <div>
                        <Label for="reg-username">Username</Label>
                        <Input id="reg-username" v-model="registerData.username" type="text" class="mt-1" />
                    </div>
                    <div>
                        <Label for="reg-email">Email</Label>
                        <Input id="reg-email" v-model="registerData.email" type="email" class="mt-1" />
                    </div>
                    <div>
                        <Label for="reg-password">Password</Label>
                        <Input id="reg-password" v-model="registerData.password" type="password" class="mt-1" />
                    </div>
                    <Button class="w-full mt-2" @click="handleRegister" :disabled="loading">
                        <span v-if="loading" class="animate-spin mr-2">⠋</span>
                        Create Account
                    </Button>
                    <Button variant="ghost" class="w-full" @click="showRegister = false">Cancel</Button>
                </div>
            </div>
        </div>

        <!-- Forgot Password Dialog -->
        <div v-if="showForgotPassword" class="fixed inset-0 bg-black/50 flex items-center justify-center z-50" @click.self="showForgotPassword = false">
            <div class="bg-card rounded-2xl p-8 w-full max-w-sm shadow-xl">
                <h3 class="text-xl font-bold mb-2 text-center">Reset Password</h3>
                <p class="text-sm text-muted-foreground text-center mb-6">Enter your username or email and we'll send you a link to reset your password.</p>
                <div class="grid gap-4">
                    <div>
                        <Label for="forgot-input">Username or Email</Label>
                        <Input id="forgot-input" v-model="forgotPasswordInput" type="text" class="mt-1" @keyup.enter="handleForgotPassword" />
                    </div>
                    <Button class="w-full mt-2" @click="handleForgotPassword" :disabled="sendingResetEmail">
                        <span v-if="sendingResetEmail" class="animate-spin mr-2">⠋</span>
                        Send Reset Link
                    </Button>
                    <Button variant="ghost" class="w-full" @click="showForgotPassword = false">Cancel</Button>
                </div>
            </div>
        </div>
    </div>

    <!-- Main App -->
    <div v-else class="app-container flex h-screen bg-background text-foreground transition-colors duration-300">
        <!-- Sidebar Wrap to give floating effect space -->
        <div class="sidebar-wrapper p-4 pr-0 hidden md:flex flex-col h-full shrink-0">
            <div
                class="bg-zinc-50 dark:bg-zinc-950 text-zinc-800 dark:text-zinc-50 rounded-[1.25rem] h-full flex flex-col shadow-xl border border-zinc-200 dark:border-zinc-800 overflow-hidden relative transition-all duration-300 ease-in-out"
                :class="isSidebarHovered ? 'w-[260px]' : 'w-[68px]'"
                @mouseenter="isSidebarHovered = true"
                @mouseleave="isSidebarHovered = false"
            >
                <!-- Header -->
                <div class="py-4 flex items-center border-b border-zinc-200 dark:border-zinc-800 transition-all duration-300 h-[53px]" :class="isSidebarHovered ? 'justify-between px-5' : 'justify-center px-0'">
                    <span class="font-medium text-[13px] tracking-wide text-zinc-800 dark:text-zinc-100 whitespace-nowrap transition-all duration-300" :class="isSidebarHovered ? 'opacity-100 w-auto' : 'opacity-0 w-0 overflow-hidden'"> Menu </span>
                    <AlignRight class="w-4 h-4 text-zinc-500 dark:text-zinc-400 shrink-0" />
                </div>

                <!-- Nav Items -->
                <div class="flex-col p-2 gap-1 flex overflow-y-auto mt-1 custom-scrollbar">
                    <!-- Home -->
                    <div
                        class="group flex items-center p-3 rounded-xl border cursor-pointer transition-all duration-200"
                        :class="[
                            isActive('/') 
                                ? 'bg-primary text-primary-foreground border-primary/10 shadow-lg shadow-primary/10 font-medium' 
                                : 'border-transparent text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-900/50', 
                            isSidebarHovered ? 'justify-start' : 'justify-center'
                        ]"
                        @click="navigateTo('/')"
                        title="Home"
                    >
                        <Home class="w-5 h-5 shrink-0 transition-colors" :class="isActive('/') ? 'text-primary-foreground' : 'text-zinc-400 group-hover:text-zinc-600 dark:group-hover:text-zinc-200'" />
                        <span class="overflow-hidden whitespace-nowrap transition-all duration-300 text-[13px]" :class="[isSidebarHovered ? 'opacity-100 ml-3 w-auto' : 'opacity-0 w-0 ml-0', isActive('/') ? 'text-primary-foreground' : 'text-zinc-600 dark:text-zinc-400 group-hover:text-zinc-900 dark:group-hover:text-zinc-200']">
                            Home
                        </span>
                    </div>

                    <!-- Projects section header -->
                    <div class="overflow-hidden whitespace-nowrap transition-all duration-300" :class="isSidebarHovered ? 'opacity-100' : 'opacity-0 h-0'">
                        <div class="flex items-center justify-between px-3 py-1 mt-2">
                            <span class="text-[10px] font-semibold uppercase tracking-wider text-zinc-400 dark:text-zinc-500">Projects</span>
                            <button
                                class="w-5 h-5 flex items-center justify-center rounded hover:bg-zinc-200 dark:hover:bg-zinc-800 text-zinc-400 dark:text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-300 transition-colors"
                                @click.stop="openNewProject()"
                                title="New project"
                            >
                                <Plus class="w-3 h-3" />
                            </button>
                        </div>
                    </div>

                    <!-- Collapsed state: show project icons -->
                    <div v-if="!isSidebarHovered" class="flex flex-col gap-1">
                        <div
                            v-for="project in projectStore.projects"
                            :key="project.id"
                            class="w-9 h-9 mx-auto flex items-center justify-center rounded-lg cursor-pointer transition-colors"
                            :class="projectStore.currentProjectId === project.id 
                                ? 'bg-primary/20 text-primary border border-primary/30' 
                                : 'text-zinc-500 hover:text-zinc-800 dark:hover:text-zinc-200 hover:bg-zinc-200 dark:hover:bg-zinc-800'"
                            :title="project.name"
                            @click="projectStore.toggleExpanded(project.id)"
                        >
                            <span class="text-[11px] font-bold">{{ project.name.charAt(0).toUpperCase() }}</span>
                        </div>
                    </div>

                    <!-- Expanded state: full project tree -->
                    <div v-if="isSidebarHovered" class="flex flex-col gap-0.5">
                        <div v-for="project in projectStore.projects" :key="project.id">
                            <!-- Project row -->
                            <div
                                class="group flex items-center gap-2 px-2 py-2 rounded-xl cursor-pointer transition-colors"
                                :class="projectStore.currentProjectId === project.id 
                                    ? 'bg-primary/10 text-primary font-medium' 
                                    : 'text-zinc-600 dark:text-zinc-400 hover:text-zinc-800 dark:hover:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-900/50'"
                                @click="projectStore.toggleExpanded(project.id)"
                            >
                                <ChevronDown v-if="projectStore.isExpanded(project.id)" class="w-3.5 h-3.5 shrink-0 text-zinc-400 dark:text-zinc-500" />
                                <ChevronRight v-else class="w-3.5 h-3.5 shrink-0 text-zinc-400 dark:text-zinc-500" />
                                <span class="text-[13px] flex-1 truncate">{{ project.name }}</span>
                                <button
                                    class="opacity-0 group-hover:opacity-100 w-5 h-5 flex items-center justify-center rounded hover:bg-zinc-200 dark:hover:bg-zinc-800 text-zinc-400 dark:text-zinc-500 hover:text-zinc-700 dark:hover:text-zinc-300 transition-all"
                                    @click.stop="openEditProject(project)"
                                    title="Edit project"
                                >
                                    <Pencil class="w-3 h-3" />
                                </button>
                            </div>

                            <!-- Sub-items -->
                            <div v-if="projectStore.isExpanded(project.id)" class="ml-4 flex flex-col gap-0.5 mb-1 pl-2 border-l border-zinc-200 dark:border-zinc-800">
                                <div
                                    v-for="subItem in projectSubItems"
                                    :key="subItem.key"
                                    class="group flex items-center gap-2.5 px-2 py-1.5 rounded-lg cursor-pointer transition-colors"
                                    :class="isActive(subItem.path) && projectStore.currentProjectId === project.id
                                        ? 'bg-primary/15 text-primary font-semibold'
                                        : 'text-zinc-500 dark:text-zinc-400 hover:text-zinc-800 dark:hover:text-zinc-200 hover:bg-zinc-100 dark:hover:bg-zinc-900/50'"
                                    @click="selectProjectItem(project, subItem)"
                                >
                                    <component :is="subItem.icon" class="w-3.5 h-3.5 shrink-0" />
                                    <span class="text-[12px]">{{ subItem.label }}</span>
                                </div>
                            </div>
                        </div>

                        <div v-if="projectStore.projects.length === 0" class="px-3 py-2 text-[11px] text-zinc-500 italic">
                            No projects yet
                        </div>
                    </div>

                </div>

                <!-- Bottom items -->
                <div class="mt-auto p-2 border-t border-zinc-200 dark:border-zinc-800 flex flex-col gap-2">
                    <div
                        class="group flex items-center justify-center p-3 rounded-lg border border-zinc-200 dark:border-zinc-800 hover:bg-zinc-100 dark:hover:bg-zinc-900 transition-all duration-200 cursor-pointer overflow-hidden whitespace-nowrap"
                        @click="logout"
                        :title="!isSidebarHovered ? 'Cerrar Sesión' : ''"
                    >
                        <LogOut class="w-5 h-5 shrink-0 text-zinc-400 dark:text-zinc-500 group-hover:text-zinc-800 dark:group-hover:text-zinc-300" />
                        <span class="text-[13px] font-medium text-zinc-600 dark:text-zinc-300 group-hover:text-zinc-900 dark:group-hover:text-zinc-50 transition-all duration-300" :class="isSidebarHovered ? 'opacity-100 w-auto ml-2' : 'opacity-0 w-0 ml-0'"> Cerrar Sesión </span>
                    </div>
                </div>
            </div>
        </div>

        <!-- Project Dialog -->
        <div v-if="showProjectDialog" class="fixed inset-0 bg-black/60 flex items-center justify-center z-50" @click.self="showProjectDialog = false">
            <div class="bg-zinc-950 border border-zinc-800 rounded-2xl p-6 w-full max-w-sm shadow-xl text-zinc-100">
                <h3 class="text-lg font-semibold text-zinc-100 mb-4">{{ isEditingProject ? 'Edit Project' : 'New Project' }}</h3>
                <div class="flex flex-col gap-3">
                    <div>
                        <Label class="text-zinc-400 text-xs mb-1 block">Name</Label>
                        <Input v-model="editingProject.name" placeholder="Project name" class="bg-zinc-900 border-zinc-800 text-zinc-100" @keyup.enter="saveProject" />
                    </div>
                    <div>
                        <Label class="text-zinc-400 text-xs mb-1 block">Description</Label>
                        <Input v-model="editingProject.description" placeholder="Optional description" class="bg-zinc-900 border-zinc-800 text-zinc-100" />
                    </div>

                    <div v-if="isEditingProject" class="border-t border-zinc-800 pt-3 mt-1">
                        <Label class="text-zinc-400 text-xs mb-1 block">Members (see everything in this project)</Label>
                        <div class="flex gap-1.5">
                            <Input v-model="memberQuery" placeholder="Username or email" class="bg-zinc-900 border-zinc-800 text-zinc-100 text-sm" @keyup.enter="addProjectMember" />
                            <select v-model="memberPermission" class="h-9 rounded-md border border-zinc-800 bg-zinc-900 text-zinc-100 px-2 text-sm">
                                <option value="view">Can view</option>
                                <option value="edit">Can edit</option>
                            </select>
                            <Button variant="outline" size="icon" class="border-zinc-800 shrink-0" @click="addProjectMember" :disabled="!memberQuery.trim() || addingMember" title="Add member">
                                <Loader2 v-if="addingMember" class="w-4 h-4 animate-spin" /><UserPlus v-else class="w-4 h-4" />
                            </Button>
                        </div>
                        <div v-if="projectMembers.length" class="space-y-1 mt-2">
                            <div v-for="member in projectMembers" :key="member.granteeUserId || member.GranteeUserId"
                                 class="flex items-center justify-between text-xs bg-zinc-900 rounded px-2 py-1.5">
                                <div class="min-w-0 truncate">
                                    <span class="font-medium">{{ member.fullName || member.FullName || member.username || member.Username }}</span>
                                    <span class="text-zinc-500 ml-1">({{ member.permission || member.Permission }})</span>
                                </div>
                                <button class="text-zinc-500 hover:text-red-400 shrink-0 ml-2" @click="removeProjectMember(member.granteeUserId || member.GranteeUserId)" title="Remove member">
                                    <X class="w-3.5 h-3.5" />
                                </button>
                            </div>
                        </div>
                        <p v-else-if="!loadingProjectMembers" class="text-xs text-zinc-500 mt-2">No members yet -- just you.</p>
                    </div>

                    <div class="flex gap-2 mt-2">
                        <Button class="flex-1" @click="saveProject">Save</Button>
                        <Button variant="outline" class="flex-1" @click="showProjectDialog = false">Cancel</Button>
                    </div>
                    <Button v-if="isEditingProject" variant="destructive" class="w-full" @click="deleteProject(editingProject.id)">
                        <Trash2 class="w-4 h-4 mr-2" />
                        Delete Project
                    </Button>
                </div>
            </div>
        </div>

        <!-- Main Content -->
        <div class="main-content flex-1 flex flex-col min-w-0">
            <!-- Top Bar -->
            <div class="search-bar h-14 border-b bg-background flex items-center px-4 gap-4 sticky top-0 z-10 transition-colors duration-300">
                <!-- Current project name or Welcome -->
                <div class="flex items-center gap-3 shrink-0">
                    <span class="text-[15px] font-semibold text-foreground leading-none truncate max-w-[180px]">
                        {{ projectStore.currentProject?.name || 'Welcome' }}
                    </span>
                    <div class="h-4 w-px bg-border"></div>
                </div>
                
                <div class="flex items-center gap-2 ml-2 pl-4 ">
                    <div class="member-avatar w-8 h-8 rounded-full bg-primary text-primary-foreground flex items-center justify-center text-sm font-medium">
                        {{ userInitial }}
                    </div>
                    <span class="font-medium text-sm hidden sm:block">{{ userName }}</span>
                </div>
                <button @click="openSettings" class="p-2 rounded-lg text-muted-foreground hover:text-foreground hover:bg-muted/60 transition-colors" title="Settings">
                    <Settings class="w-4.5 h-4.5" />
                </button>
            </div>

            <!-- Router View -->
            <div class="flex-1 overflow-auto p-4 md:p-6 lg:p-8">
                <RouterView />
            </div>
        </div>
    </div>

    <!-- Settings Dialog — uses same plain fixed-overlay pattern as Project/Register dialogs -->
    <div v-if="showSettings" class="fixed inset-0 bg-black/60 flex items-center justify-center z-50" @click.self="showSettings = false">
        <div class="bg-background border rounded-2xl p-6 w-full max-w-lg shadow-xl mx-4 relative">
            <!-- Header -->
            <div class="flex items-center justify-between mb-4">
                <h3 class="text-lg font-semibold flex items-center gap-2">
                    <Settings class="w-4 h-4" /> Settings
                </h3>
                <button @click="showSettings = false" class="text-muted-foreground hover:text-foreground transition-colors">
                    <X class="w-4 h-4" />
                </button>
            </div>

            <!-- Tab nav -->
            <div class="flex gap-1 bg-muted p-1 rounded-md mb-4">
                <button
                    v-for="tab in settingsTabs"
                    :key="tab.id"
                    @click="settingsTab = tab.id"
                    class="flex-1 px-3 py-1.5 text-sm font-medium rounded-sm transition-colors"
                    :class="settingsTab === tab.id ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'"
                >
                    {{ tab.label }}
                </button>
            </div>

            <!-- Profile tab -->
            <div v-if="settingsTab === 'profile'" class="space-y-4">
                <div class="space-y-2">
                    <Label>Display Name</Label>
                    <Input v-model="settingsDisplayName" placeholder="Enter new display name" @keyup.enter="saveProfile" />
                    <p class="text-xs text-muted-foreground">This name appears next to your avatar in the header.</p>
                </div>
                <Button @click="saveProfile" :disabled="!settingsDisplayName.trim() || savingProfile" class="w-full">
                    {{ savingProfile ? 'Saving...' : 'Update Display Name' }}
                </Button>
            </div>

            <!-- Password tab -->
            <div v-if="settingsTab === 'password'" class="space-y-4">
                <div class="space-y-2">
                    <Label>Current Password</Label>
                    <div class="relative">
                        <Input v-model="settingsOldPassword" :type="showOldPw ? 'text' : 'password'" placeholder="Enter current password" class="pr-10" />
                        <button type="button" class="absolute right-3 top-2.5 text-muted-foreground" @click="showOldPw = !showOldPw">
                            <EyeOff v-if="showOldPw" class="w-4 h-4" /><Eye v-else class="w-4 h-4" />
                        </button>
                    </div>
                </div>
                <div class="space-y-2">
                    <Label>New Password</Label>
                    <div class="relative">
                        <Input v-model="settingsNewPassword" :type="showNewPw ? 'text' : 'password'" placeholder="Enter new password" class="pr-10" />
                        <button type="button" class="absolute right-3 top-2.5 text-muted-foreground" @click="showNewPw = !showNewPw">
                            <EyeOff v-if="showNewPw" class="w-4 h-4" /><Eye v-else class="w-4 h-4" />
                        </button>
                    </div>
                </div>
                <div class="space-y-2">
                    <Label>Confirm New Password</Label>
                    <div class="relative">
                        <Input v-model="settingsConfirmPassword" :type="showConfirmPw ? 'text' : 'password'" placeholder="Confirm new password" class="pr-10" />
                        <button type="button" class="absolute right-3 top-2.5 text-muted-foreground" @click="showConfirmPw = !showConfirmPw">
                            <EyeOff v-if="showConfirmPw" class="w-4 h-4" /><Eye v-else class="w-4 h-4" />
                        </button>
                    </div>
                    <p v-if="settingsConfirmPassword && settingsNewPassword !== settingsConfirmPassword" class="text-xs text-destructive">Passwords do not match</p>
                </div>
                <Button @click="changePassword" :disabled="savingPassword || !settingsOldPassword || !settingsNewPassword || settingsNewPassword !== settingsConfirmPassword" class="w-full">
                    {{ savingPassword ? 'Changing...' : 'Change Password' }}
                </Button>
            </div>

            <!-- Email (SMTP) tab -->
            <div v-if="settingsTab === 'smtp'" class="space-y-4">
                <p class="text-xs text-muted-foreground">Used to send password-reset emails and dashboard share links. Applies to the whole app, not just your account.</p>
                <div v-if="smtpLoading" class="text-sm text-muted-foreground py-4 text-center">Loading...</div>
                <template v-else>
                    <div class="grid grid-cols-3 gap-3">
                        <div class="col-span-2 space-y-2">
                            <Label>SMTP Host</Label>
                            <Input v-model="smtpConfig.host" placeholder="smtp.gmail.com" />
                        </div>
                        <div class="space-y-2">
                            <Label>Port</Label>
                            <Input v-model.number="smtpConfig.port" type="number" placeholder="587" />
                        </div>
                    </div>
                    <div class="space-y-2">
                        <Label>From Address</Label>
                        <Input v-model="smtpConfig.fromAddress" type="email" placeholder="noreply@company.com" />
                    </div>
                    <div class="space-y-2">
                        <Label>Username</Label>
                        <Input v-model="smtpConfig.username" placeholder="smtp_user" />
                    </div>
                    <div class="space-y-2">
                        <Label>Password</Label>
                        <Input v-model="smtpConfig.password" type="password" :placeholder="smtpHasPassword ? 'Leave blank to keep current password' : 'Enter password'" />
                    </div>
                    <div class="flex items-center gap-2">
                        <Switch id="smtp-ssl" :checked="smtpConfig.useSsl" @update:checked="(val) => (smtpConfig.useSsl = val)" />
                        <Label for="smtp-ssl">Use SSL/TLS</Label>
                    </div>
                    <Button @click="saveSmtpConfig" :disabled="smtpSaving || !smtpConfig.host.trim() || !smtpConfig.fromAddress.trim()" class="w-full">
                        {{ smtpSaving ? 'Saving...' : 'Save Email Settings' }}
                    </Button>
                </template>
            </div>

            <!-- Users tab (admin only) -->
            <div v-if="settingsTab === 'users'" class="space-y-3">
                <p class="text-xs text-muted-foreground">Manage account roles and access. Viewers can open and run existing dashboards/reports but cannot create, edit, delete, or share content.</p>
                <div v-if="usersLoading" class="text-sm text-muted-foreground py-4 text-center">Loading...</div>
                <div v-else class="space-y-2 max-h-96 overflow-y-auto custom-scrollbar">
                    <div v-for="user in usersList" :key="user.userId" class="border rounded-md p-3 space-y-2" :class="{ 'opacity-50': !user.isActive }">
                        <div class="flex items-center justify-between">
                            <div>
                                <p class="text-sm font-medium">{{ user.username }}</p>
                                <p class="text-xs text-muted-foreground">{{ user.email }}</p>
                            </div>
                            <button
                                @click="toggleUserActive(user)"
                                class="text-xs px-2 py-1 rounded-md border"
                                :class="user.isActive ? 'text-destructive hover:bg-destructive/10' : 'text-primary hover:bg-primary/10'"
                            >
                                {{ user.isActive ? 'Deactivate' : 'Activate' }}
                            </button>
                        </div>
                        <div class="flex gap-1">
                            <button
                                v-for="role in ['viewer', 'user', 'admin']"
                                :key="role"
                                @click="changeUserRole(user, role)"
                                :disabled="userRoleUpdating === user.userId"
                                class="flex-1 px-2 py-1 text-xs rounded-md border capitalize transition-colors"
                                :class="user.roles === role ? 'bg-primary text-primary-foreground border-primary' : 'text-muted-foreground hover:bg-muted'"
                            >
                                {{ role === 'user' ? 'Editor' : role }}
                            </button>
                        </div>
                    </div>
                    <p v-if="!usersList.length" class="text-sm text-muted-foreground text-center py-4">No users found.</p>
                </div>
            </div>

            <!-- Audit Log tab -->
            <div v-if="settingsTab === 'audit'" class="space-y-3">
                <p class="text-xs text-muted-foreground">Recent create/edit/delete/share activity across the instance, newest first.</p>
                <div v-if="auditLoading && !auditLogEntries.length" class="text-sm text-muted-foreground py-4 text-center">Loading...</div>
                <div v-else class="space-y-1.5 max-h-96 overflow-y-auto custom-scrollbar">
                    <div v-for="entry in auditLogEntries" :key="entry.id || entry.Id" class="border rounded-md p-2.5 text-xs">
                        <div class="flex items-center justify-between gap-2">
                            <span class="font-medium truncate">{{ entry.username || entry.Username || 'Unknown user' }}</span>
                            <span class="text-muted-foreground shrink-0">{{ new Date(entry.createdAt || entry.CreatedAt).toLocaleString() }}</span>
                        </div>
                        <p class="text-muted-foreground mt-0.5">
                            <span class="capitalize">{{ entry.action || entry.Action }}</span>
                            <template v-if="entry.resourceType || entry.ResourceType">
                                — {{ entry.resourceType || entry.ResourceType }}<span v-if="entry.resourceName || entry.ResourceName">: {{ entry.resourceName || entry.ResourceName }}</span>
                            </template>
                            <template v-if="entry.details || entry.Details"> ({{ entry.details || entry.Details }})</template>
                        </p>
                    </div>
                    <p v-if="!auditLogEntries.length" class="text-sm text-muted-foreground text-center py-4">No activity recorded yet.</p>
                    <button v-if="auditHasMore" @click="loadAuditLog(true)" :disabled="auditLoading"
                            class="w-full text-xs text-center py-2 text-primary hover:underline disabled:opacity-50">
                        {{ auditLoading ? 'Loading...' : 'Load more' }}
                    </button>
                </div>
            </div>

            <!-- Appearance tab -->
            <div v-if="settingsTab === 'appearance'" class="space-y-5">
                <div class="flex items-center justify-between">
                    <div>
                        <p class="text-sm font-medium">Theme</p>
                        <p class="text-xs text-muted-foreground">Switch between light and dark mode</p>
                    </div>
                    <button
                        @click="toggleDarkMode"
                        class="flex items-center gap-2 px-3 py-1.5 rounded-md border bg-background hover:bg-muted transition-colors text-sm font-medium"
                    >
                        <Moon v-if="!layoutConfig.darkMode" class="w-4 h-4" />
                        <Sun v-else class="w-4 h-4" />
                        {{ layoutConfig.darkMode ? 'Switch to Light' : 'Switch to Dark' }}
                    </button>
                </div>

                <div class="space-y-3">
                    <div>
                        <p class="text-sm font-medium">Accent Color</p>
                        <p class="text-xs text-muted-foreground">Primary color used across buttons and highlights</p>
                    </div>
                    <div class="flex gap-4 flex-wrap">
                        <button
                            v-for="color in accentColors"
                            :key="color.name"
                            @click="setThemeColor(color.name)"
                            class="flex flex-col items-center gap-1.5"
                            :title="color.label"
                        >
                            <div
                                class="w-9 h-9 rounded-full flex items-center justify-center ring-2 ring-offset-2 ring-offset-background transition-all"
                                :class="[color.cls, layoutConfig.themeColor === color.name ? 'ring-foreground scale-110' : 'ring-transparent']"
                            >
                                <Check v-if="layoutConfig.themeColor === color.name" class="w-4 h-4 text-white" />
                            </div>
                            <span class="text-xs text-muted-foreground">{{ color.label }}</span>
                        </button>
                    </div>
                </div>
            </div>
        </div>
    </div>
</template>

<style scoped>
/* Scoped styles if needed - most styles are in app-style.css */
.custom-scrollbar::-webkit-scrollbar {
    width: 4px;
}
.custom-scrollbar::-webkit-scrollbar-track {
    background: transparent;
}
.custom-scrollbar::-webkit-scrollbar-thumb {
    background: #3f3f46;
    border-radius: 4px;
}
.custom-scrollbar::-webkit-scrollbar-thumb:hover {
    background: #52525b;
}
.setup-fullscreen {
    width: 100vw;
    min-height: 100vh;
}
</style>
