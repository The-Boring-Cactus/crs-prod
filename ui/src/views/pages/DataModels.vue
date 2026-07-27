<script setup>
import { onMounted, ref, computed, getCurrentInstance, watch } from 'vue';
import { useDatabaseStore } from '@/store/databaseStore';
import { useProjectStore } from '@/store/projectStore';
import { userStoreMe } from '@/store/userStore';
import { toast } from 'vue-sonner';
import { Toaster } from '@/components/ui/sonner';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Dialog, DialogContent, DialogHeader, DialogTitle, DialogFooter } from '@/components/ui/dialog';
import { Network, Plus, Trash2, Pencil, Loader2, RefreshCw, ArrowRight, X, Play } from 'lucide-vue-next';
import DataModelQueryPanel from '@/components/datamodel/DataModelQueryPanel.vue';
import { createDefaultQuery } from '@/components/datamodel/defaultQuery';

const databaseStore = useDatabaseStore();
const projectStore = useProjectStore();
const userStore = userStoreMe();
const { proxy } = getCurrentInstance();

const models = ref([]);
const loadingModels = ref(false);
const builderOpen = ref(false);
const savingModel = ref(false);
const deleteTarget = ref(null);

// Builder state
const editingModelId = ref(null);
const editingModelProjectId = ref(null); // preserves the model's own project on re-save, in case the active project changed since it was loaded
const modelName = ref('');
const selectedConnectionId = ref('');
const loadingSchema = ref(false);
const schemaTables = ref([]); // [{ tableName, columns: [{columnName, dataType}] }]
const selectedTables = ref([]); // [{ alias, tableName }]
const relationships = ref([]); // [{ fromTable, fromColumn, toTable, toColumn, joinType }]

// SaveDataModel/LoadDataModels go through the generic entity-storage layer
// (DatabasePersistence.SaveEntity/LoadEntities), which serializes the *whole*
// saved object into its own "Config" column rather than storing connectionId/
// tables/relationships as top-level row fields -- so those three have to be
// read back out of a nested, re-parsed JSON string, not off the row directly.
function unpackModelRow(m) {
    const configRaw = m.config ?? m.Config;
    let parsed = {};
    if (typeof configRaw === 'string') {
        try { parsed = JSON.parse(configRaw); } catch { parsed = {}; }
    } else if (configRaw && typeof configRaw === 'object') {
        parsed = configRaw;
    }
    return {
        id: m.id || m.Id || parsed.id,
        name: m.name || m.Name || parsed.name,
        connectionId: parsed.connectionId,
        tables: parsed.tables || [],
        relationships: parsed.relationships || [],
        projectId: parsed.projectId
    };
}

async function loadModels() {
    loadingModels.value = true;
    try {
        const result = await userStore.executeCommand('LoadDataModels', { projectId: projectStore.currentProjectId }, proxy.$socket);
        models.value = (result?.Data || result?.data || []).map(unpackModelRow);
    } catch (e) {
        toast.error('Failed to load data models', { description: e.message });
    } finally {
        loadingModels.value = false;
    }
}

function connectionName(connectionId) {
    return databaseStore.allConnections.find((c) => c.id === connectionId)?.name || connectionId;
}

function openNewModel() {
    editingModelId.value = null;
    editingModelProjectId.value = null;
    modelName.value = '';
    selectedConnectionId.value = '';
    schemaTables.value = [];
    selectedTables.value = [];
    relationships.value = [];
    builderOpen.value = true;
}

function openEditModel(model) {
    editingModelId.value = model.id;
    editingModelProjectId.value = model.projectId ?? null;
    modelName.value = model.name;
    selectedConnectionId.value = model.connectionId || '';
    schemaTables.value = [];
    selectedTables.value = [...model.tables];
    relationships.value = model.relationships.map((r) => ({ ...r }));
    builderOpen.value = true;
    if (selectedConnectionId.value) loadSchema();
}

async function loadSchema() {
    if (!selectedConnectionId.value) {
        toast.error('Select a database connection first');
        return;
    }
    loadingSchema.value = true;
    try {
        const result = await userStore.executeCommand('ListTables', { connectionId: selectedConnectionId.value }, proxy.$socket);
        schemaTables.value = result?.Data || result?.data || [];
        if (!schemaTables.value.length) {
            toast.error('No tables found. Check the connection and that it has permission to read the schema.');
        }
    } catch (e) {
        toast.error('Failed to load schema', { description: e.message });
    } finally {
        loadingSchema.value = false;
    }
}

function isTableSelected(tableName) {
    return selectedTables.value.some((t) => t.tableName === tableName);
}

function toggleTable(tableName) {
    const idx = selectedTables.value.findIndex((t) => t.tableName === tableName);
    if (idx >= 0) {
        const alias = selectedTables.value[idx].alias;
        selectedTables.value.splice(idx, 1);
        relationships.value = relationships.value.filter((r) => r.fromTable !== alias && r.toTable !== alias);
    } else {
        selectedTables.value.push({ alias: tableName, tableName });
    }
}

function columnsFor(alias) {
    const table = selectedTables.value.find((t) => t.alias === alias);
    if (!table) return [];
    return schemaTables.value.find((t) => t.tableName === table.tableName)?.columns || [];
}

function addRelationship() {
    if (selectedTables.value.length < 2) {
        toast.error('Select at least 2 tables before defining a relationship');
        return;
    }
    relationships.value.push({
        fromTable: selectedTables.value[0].alias,
        fromColumn: '',
        toTable: selectedTables.value[1].alias,
        toColumn: '',
        joinType: 'inner'
    });
}

function removeRelationship(index) {
    relationships.value.splice(index, 1);
}

async function saveModel() {
    if (!modelName.value.trim()) { toast.error('Name is required'); return; }
    if (!selectedConnectionId.value) { toast.error('Select a database connection'); return; }
    if (selectedTables.value.length === 0) { toast.error('Select at least one table'); return; }
    for (const r of relationships.value) {
        if (!r.fromColumn || !r.toColumn) { toast.error('Every relationship needs both columns set'); return; }
    }

    savingModel.value = true;
    try {
        const payload = {
            id: editingModelId.value || undefined,
            name: modelName.value.trim(),
            connectionId: selectedConnectionId.value,
            tables: selectedTables.value,
            relationships: relationships.value,
            // Without this, SaveEntity() stores a NULL ProjectId, and LoadDataModels'
            // "WHERE ProjectId = @ProjectId" filter (used whenever a project is active)
            // then never matches it -- the model exists in the DB but the list looks
            // empty. Mirrors the same stamp-on-save pattern databaseStore.ts uses.
            projectId: editingModelProjectId.value ?? projectStore.currentProjectId ?? undefined
        };
        await userStore.executeCommand('SaveDataModel', { model: payload }, proxy.$socket);
        toast.success('Data model saved');
        builderOpen.value = false;
        await loadModels();
    } catch (e) {
        toast.error('Failed to save data model', { description: e.message });
    } finally {
        savingModel.value = false;
    }
}

// Preview: build + run a query against a saved model's tables using DataModelQueryBuilder
const previewOpen = ref(false);
const previewModel = ref(null);
const previewColumns = ref({}); // { [alias]: [{columnName, dataType}] }
const previewLoadingSchema = ref(false);
const previewQuery = ref(createDefaultQuery());
const previewRunning = ref(false);
const previewResult = ref(null); // { rows, columns } from RunDataModelQuery

async function openPreview(model) {
    previewModel.value = model;
    previewQuery.value = createDefaultQuery();
    previewResult.value = null;
    previewColumns.value = {};
    previewOpen.value = true;

    if (!model.connectionId) return;
    previewLoadingSchema.value = true;
    try {
        const result = await userStore.executeCommand('ListTables', { connectionId: model.connectionId }, proxy.$socket);
        const tablesByName = {};
        for (const t of result?.Data || result?.data || []) tablesByName[t.tableName] = t.columns || [];
        const columnsByAlias = {};
        for (const t of model.tables) columnsByAlias[t.alias] = tablesByName[t.tableName] || [];
        previewColumns.value = columnsByAlias;
    } catch (e) {
        toast.error('Failed to load schema', { description: e.message });
    } finally {
        previewLoadingSchema.value = false;
    }
}

async function runPreview() {
    if (!previewModel.value) return;
    previewRunning.value = true;
    try {
        const result = await userStore.executeCommand('RunDataModelQuery', { modelId: previewModel.value.id, ...previewQuery.value }, proxy.$socket);
        previewResult.value = result?.Data || null;
    } catch (e) {
        toast.error('Query failed', { description: e.message });
        previewResult.value = null;
    } finally {
        previewRunning.value = false;
    }
}

function confirmDelete(model) {
    deleteTarget.value = model;
}

async function deleteModel() {
    if (!deleteTarget.value) return;
    try {
        await userStore.executeCommand('DeleteDataModel', { id: deleteTarget.value.id }, proxy.$socket);
        toast.success('Data model deleted');
        deleteTarget.value = null;
        await loadModels();
    } catch (e) {
        toast.error('Failed to delete data model', { description: e.message });
    }
}

onMounted(() => {
    databaseStore.loadConnections(proxy.$socket);
    loadModels();
});

watch(() => projectStore.currentProjectId, () => {
    databaseStore.loadConnections(proxy.$socket);
    loadModels();
});
</script>

<template>
    <Toaster richColors position="top-right" />
    <div class="p-4 space-y-4">
        <div class="flex items-center justify-between">
            <div>
                <h1 class="text-xl font-semibold flex items-center gap-2"><Network class="w-5 h-5" /> Data Models</h1>
                <p class="text-sm text-muted-foreground">Define tables and relationships once, then reuse them across dashboard widgets instead of writing joins every time.</p>
            </div>
            <Button v-if="userStore.canEdit" @click="openNewModel" class="gap-2"><Plus class="w-4 h-4" /> New Data Model</Button>
        </div>

        <div v-if="loadingModels" class="text-sm text-muted-foreground py-8 text-center">Loading...</div>
        <div v-else-if="!models.length" class="border rounded-md p-8 text-center text-sm text-muted-foreground">
            No data models yet. {{ userStore.canEdit ? 'Create one to get started.' : '' }}
        </div>
        <div v-else class="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-3">
            <div v-for="model in models" :key="model.id" class="border rounded-md p-4 space-y-2">
                <div class="flex items-start justify-between">
                    <div>
                        <p class="font-medium">{{ model.name }}</p>
                        <p class="text-xs text-muted-foreground">{{ connectionName(model.connectionId) }}</p>
                    </div>
                    <div v-if="userStore.canEdit" class="flex gap-1">
                        <Button variant="ghost" size="icon" class="h-7 w-7" title="Preview / Run Query" @click="openPreview(model)"><Play class="w-3.5 h-3.5" /></Button>
                        <Button variant="ghost" size="icon" class="h-7 w-7" @click="openEditModel(model)"><Pencil class="w-3.5 h-3.5" /></Button>
                        <Button variant="ghost" size="icon" class="h-7 w-7 text-destructive hover:bg-destructive/10" @click="confirmDelete(model)"><Trash2 class="w-3.5 h-3.5" /></Button>
                    </div>
                </div>
                <p class="text-xs text-muted-foreground">{{ model.tables.length }} table(s), {{ model.relationships.length }} relationship(s)</p>
                <div class="flex flex-wrap gap-1">
                    <span v-for="t in model.tables" :key="t.alias" class="text-xs bg-muted px-2 py-0.5 rounded-full">{{ t.alias }}</span>
                </div>
            </div>
        </div>

        <!-- Builder Dialog -->
        <Dialog v-model:open="builderOpen">
            <DialogContent class="max-w-3xl max-h-[85vh] overflow-y-auto">
                <DialogHeader>
                    <DialogTitle>{{ editingModelId ? 'Edit Data Model' : 'New Data Model' }}</DialogTitle>
                </DialogHeader>

                <div class="space-y-4">
                    <div class="space-y-2">
                        <Label>Name</Label>
                        <Input v-model="modelName" placeholder="e.g. Sales Model" />
                    </div>

                    <div class="space-y-2">
                        <Label>Database Connection</Label>
                        <div class="flex gap-2">
                            <select v-model="selectedConnectionId" class="flex-1 border rounded-md px-2 py-1.5 text-sm bg-background">
                                <option value="" disabled>Select a connection</option>
                                <option v-for="c in databaseStore.allConnections" :key="c.id" :value="c.id">{{ c.name }} ({{ c.type }})</option>
                            </select>
                            <Button variant="outline" :disabled="loadingSchema || !selectedConnectionId" @click="loadSchema" class="gap-1.5">
                                <Loader2 v-if="loadingSchema" class="w-4 h-4 animate-spin" /><RefreshCw v-else class="w-4 h-4" /> Load Schema
                            </Button>
                        </div>
                    </div>

                    <div v-if="schemaTables.length" class="space-y-2">
                        <Label>Tables to include</Label>
                        <div class="flex flex-wrap gap-2 border rounded-md p-3">
                            <button
                                v-for="t in schemaTables"
                                :key="t.tableName"
                                @click="toggleTable(t.tableName)"
                                class="text-xs px-2.5 py-1 rounded-full border transition-colors"
                                :class="isTableSelected(t.tableName) ? 'bg-primary text-primary-foreground border-primary' : 'text-muted-foreground hover:bg-muted'"
                            >
                                {{ t.tableName }}
                            </button>
                        </div>
                    </div>

                    <div v-if="selectedTables.length >= 2" class="space-y-2">
                        <div class="flex items-center justify-between">
                            <Label>Relationships</Label>
                            <Button variant="outline" size="sm" @click="addRelationship" class="gap-1.5"><Plus class="w-3.5 h-3.5" /> Add Relationship</Button>
                        </div>
                        <div v-for="(rel, idx) in relationships" :key="idx" class="flex items-center gap-2 border rounded-md p-2">
                            <select v-model="rel.fromTable" class="border rounded-md px-2 py-1 text-xs bg-background">
                                <option v-for="t in selectedTables" :key="t.alias" :value="t.alias">{{ t.alias }}</option>
                            </select>
                            <select v-model="rel.fromColumn" class="border rounded-md px-2 py-1 text-xs bg-background">
                                <option value="" disabled>column</option>
                                <option v-for="c in columnsFor(rel.fromTable)" :key="c.columnName" :value="c.columnName">{{ c.columnName }}</option>
                            </select>
                            <ArrowRight class="w-3.5 h-3.5 text-muted-foreground shrink-0" />
                            <select v-model="rel.toTable" class="border rounded-md px-2 py-1 text-xs bg-background">
                                <option v-for="t in selectedTables" :key="t.alias" :value="t.alias">{{ t.alias }}</option>
                            </select>
                            <select v-model="rel.toColumn" class="border rounded-md px-2 py-1 text-xs bg-background">
                                <option value="" disabled>column</option>
                                <option v-for="c in columnsFor(rel.toTable)" :key="c.columnName" :value="c.columnName">{{ c.columnName }}</option>
                            </select>
                            <select v-model="rel.joinType" class="border rounded-md px-2 py-1 text-xs bg-background">
                                <option value="inner">Inner</option>
                                <option value="left">Left</option>
                                <option value="right">Right</option>
                            </select>
                            <Button variant="ghost" size="icon" class="h-7 w-7 text-destructive shrink-0" @click="removeRelationship(idx)"><X class="w-3.5 h-3.5" /></Button>
                        </div>
                    </div>
                </div>

                <DialogFooter>
                    <Button variant="outline" @click="builderOpen = false">Cancel</Button>
                    <Button @click="saveModel" :disabled="savingModel">{{ savingModel ? 'Saving...' : 'Save Data Model' }}</Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>

        <!-- Delete confirmation -->
        <Dialog :open="!!deleteTarget" @update:open="(v) => !v && (deleteTarget = null)">
            <DialogContent class="max-w-md">
                <DialogHeader><DialogTitle>Delete Data Model</DialogTitle></DialogHeader>
                <p class="text-sm text-muted-foreground">Delete "{{ deleteTarget?.name }}"? Dashboard widgets using it will stop working.</p>
                <DialogFooter>
                    <Button variant="outline" @click="deleteTarget = null">Cancel</Button>
                    <Button variant="destructive" @click="deleteModel">Delete</Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>

        <!-- Preview / Run Query -->
        <Dialog v-model:open="previewOpen">
            <DialogContent class="max-w-4xl max-h-[85vh] overflow-y-auto">
                <DialogHeader>
                    <DialogTitle>Preview: {{ previewModel?.name }}</DialogTitle>
                </DialogHeader>

                <div v-if="previewLoadingSchema" class="text-sm text-muted-foreground py-6 text-center">Loading schema...</div>
                <template v-else>
                    <DataModelQueryPanel
                        v-if="previewModel"
                        v-model="previewQuery"
                        :tables="previewModel.tables"
                        :columns-by-alias="previewColumns"
                        @run="runPreview"
                    />

                    <div class="border rounded-md overflow-auto max-h-[40vh] mt-2">
                        <div v-if="previewRunning" class="text-sm text-muted-foreground py-6 text-center">Running query...</div>
                        <div v-else-if="!previewResult" class="text-sm text-muted-foreground py-6 text-center">Run the query to see results.</div>
                        <table v-else-if="previewResult.rows?.length" class="text-xs w-full border-collapse">
                            <thead class="sticky top-0 bg-secondary z-10">
                                <tr>
                                    <th v-for="col in previewResult.columns" :key="col.field" class="border border-border px-2 py-1 text-left font-semibold whitespace-nowrap">{{ col.header }}</th>
                                </tr>
                            </thead>
                            <tbody>
                                <tr v-for="(row, ri) in previewResult.rows" :key="ri" class="hover:bg-muted/40 transition-colors">
                                    <td v-for="col in previewResult.columns" :key="col.field" class="border border-border px-2 py-1">{{ row[col.field] ?? '' }}</td>
                                </tr>
                            </tbody>
                        </table>
                        <div v-else class="text-sm text-muted-foreground py-6 text-center">No rows returned.</div>
                    </div>
                </template>

                <DialogFooter>
                    <Button variant="outline" @click="previewOpen = false">Close</Button>
                </DialogFooter>
            </DialogContent>
        </Dialog>
    </div>
</template>
