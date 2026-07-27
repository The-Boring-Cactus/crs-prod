<script setup>
import { computed } from 'vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Label } from '@/components/ui/label';
import { Plus, X, Play } from 'lucide-vue-next';

// Builds a DataModelQueryBuilder request ({fields, filters, orderBy, limit, pivot}) against
// a saved Data Model's tables. Used both as a "preview" on the Data Models page and as the
// config editor for a dashboard "Data Model" widget -- the caller owns running the query and
// rendering the { rows, columns } result, this component only builds the request.
const props = defineProps({
    tables: { type: Array, default: () => [] }, // [{ alias, tableName }]
    columnsByAlias: { type: Object, default: () => ({}) }, // { [alias]: [{ columnName, dataType }] }
    modelValue: { type: Object, required: true }
});
const emit = defineEmits(['update:modelValue', 'run']);

const AGGREGATES = [
    { value: '', label: 'None' },
    { value: 'sum', label: 'Sum' },
    { value: 'avg', label: 'Average' },
    { value: 'count', label: 'Count' },
    { value: 'min', label: 'Min' },
    { value: 'max', label: 'Max' }
];
const OPERATORS = ['=', '!=', '<', '>', '<=', '>=', 'like'];

function columnsFor(alias) {
    return props.columnsByAlias[alias] || [];
}

function firstAlias() {
    return props.tables[0]?.alias || '';
}

function update(patch) {
    emit('update:modelValue', { ...props.modelValue, ...patch });
}

// ── Fields (SELECT columns + aggregates) ──
function addField() {
    update({ fields: [...props.modelValue.fields, { table: firstAlias(), column: '', aggregate: '', alias: '' }] });
}
function removeField(index) {
    update({ fields: props.modelValue.fields.filter((_, i) => i !== index) });
}
function setField(index, patch) {
    update({ fields: props.modelValue.fields.map((f, i) => (i === index ? { ...f, ...patch } : f)) });
}

// ── Filters (WHERE) ──
function addFilter() {
    update({ filters: [...props.modelValue.filters, { table: firstAlias(), column: '', op: '=', value: '' }] });
}
function removeFilter(index) {
    update({ filters: props.modelValue.filters.filter((_, i) => i !== index) });
}
function setFilter(index, patch) {
    update({ filters: props.modelValue.filters.map((f, i) => (i === index ? { ...f, ...patch } : f)) });
}

// ── Sort (ORDER BY) ──
function addSort() {
    update({ orderBy: [...props.modelValue.orderBy, { table: firstAlias(), column: '', aggregate: '', direction: 'asc' }] });
}
function removeSort(index) {
    update({ orderBy: props.modelValue.orderBy.filter((_, i) => i !== index) });
}
function setSort(index, patch) {
    update({ orderBy: props.modelValue.orderBy.map((s, i) => (i === index ? { ...s, ...patch } : s)) });
}

// ── Pivot (rows / column / value cross-tab) ──
const isPivot = computed(() => !!props.modelValue.pivot);
function togglePivot(on) {
    if (on) {
        update({
            pivot: {
                rows: [{ table: firstAlias(), column: '' }],
                column: { table: firstAlias(), column: '' },
                value: { table: firstAlias(), column: '', aggregate: 'sum' }
            }
        });
    } else {
        update({ pivot: null });
    }
}
function addPivotRow() {
    update({ pivot: { ...props.modelValue.pivot, rows: [...props.modelValue.pivot.rows, { table: firstAlias(), column: '' }] } });
}
function removePivotRow(index) {
    update({ pivot: { ...props.modelValue.pivot, rows: props.modelValue.pivot.rows.filter((_, i) => i !== index) } });
}
function setPivotRow(index, patch) {
    const rows = props.modelValue.pivot.rows.map((r, i) => (i === index ? { ...r, ...patch } : r));
    update({ pivot: { ...props.modelValue.pivot, rows } });
}
function setPivotColumn(patch) {
    update({ pivot: { ...props.modelValue.pivot, column: { ...props.modelValue.pivot.column, ...patch } } });
}
function setPivotValue(patch) {
    update({ pivot: { ...props.modelValue.pivot, value: { ...props.modelValue.pivot.value, ...patch } } });
}
</script>

<template>
    <div class="space-y-4">
        <div class="flex items-center justify-between">
            <div class="flex bg-muted p-1 rounded-md w-fit">
                <button
                    class="px-3 py-1 text-xs font-medium rounded-sm transition-colors"
                    :class="!isPivot ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'"
                    @click="togglePivot(false)"
                >
                    Table
                </button>
                <button
                    class="px-3 py-1 text-xs font-medium rounded-sm transition-colors"
                    :class="isPivot ? 'bg-background text-foreground shadow-sm' : 'text-muted-foreground hover:text-foreground'"
                    @click="togglePivot(true)"
                >
                    Pivot
                </button>
            </div>
            <Button size="sm" variant="outline" class="gap-1.5" @click="emit('run')"><Play class="w-3.5 h-3.5" /> Run Query</Button>
        </div>

        <!-- Flat table mode: columns + aggregates -->
        <div v-if="!isPivot" class="space-y-2">
            <div class="flex items-center justify-between">
                <Label>Columns</Label>
                <Button variant="outline" size="sm" class="gap-1.5" @click="addField"><Plus class="w-3.5 h-3.5" /> Add Column</Button>
            </div>
            <p v-if="!modelValue.fields.length" class="text-xs text-muted-foreground">Add at least one column to query.</p>
            <div v-for="(f, i) in modelValue.fields" :key="i" class="flex items-center gap-2 border rounded-md p-2 flex-wrap">
                <select :value="f.table" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setField(i, { table: $event.target.value, column: '' })">
                    <option v-for="t in tables" :key="t.alias" :value="t.alias">{{ t.alias }}</option>
                </select>
                <select :value="f.column" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setField(i, { column: $event.target.value })">
                    <option value="" disabled>column</option>
                    <option v-for="c in columnsFor(f.table)" :key="c.columnName" :value="c.columnName">{{ c.columnName }}</option>
                </select>
                <select :value="f.aggregate" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setField(i, { aggregate: $event.target.value })">
                    <option v-for="a in AGGREGATES" :key="a.value" :value="a.value">{{ a.label }}</option>
                </select>
                <Input :model-value="f.alias" placeholder="alias (optional)" class="h-7 text-xs w-32" @update:model-value="(v) => setField(i, { alias: v })" />
                <Button variant="ghost" size="icon" class="h-7 w-7 text-destructive shrink-0 ml-auto" @click="removeField(i)"><X class="w-3.5 h-3.5" /></Button>
            </div>

            <div class="flex items-center justify-between pt-2">
                <Label>Sort</Label>
                <Button variant="outline" size="sm" class="gap-1.5" @click="addSort"><Plus class="w-3.5 h-3.5" /> Add Sort</Button>
            </div>
            <div v-for="(s, i) in modelValue.orderBy" :key="i" class="flex items-center gap-2 border rounded-md p-2 flex-wrap">
                <select :value="s.table" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setSort(i, { table: $event.target.value, column: '' })">
                    <option v-for="t in tables" :key="t.alias" :value="t.alias">{{ t.alias }}</option>
                </select>
                <select :value="s.column" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setSort(i, { column: $event.target.value })">
                    <option value="" disabled>column</option>
                    <option v-for="c in columnsFor(s.table)" :key="c.columnName" :value="c.columnName">{{ c.columnName }}</option>
                </select>
                <select :value="s.aggregate" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setSort(i, { aggregate: $event.target.value })">
                    <option v-for="a in AGGREGATES" :key="a.value" :value="a.value">{{ a.label }}</option>
                </select>
                <select :value="s.direction" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setSort(i, { direction: $event.target.value })">
                    <option value="asc">Ascending</option>
                    <option value="desc">Descending</option>
                </select>
                <Button variant="ghost" size="icon" class="h-7 w-7 text-destructive shrink-0 ml-auto" @click="removeSort(i)"><X class="w-3.5 h-3.5" /></Button>
            </div>
        </div>

        <!-- Pivot mode: rows / column / value -->
        <div v-else class="space-y-3">
            <div class="space-y-2">
                <div class="flex items-center justify-between">
                    <Label>Row fields</Label>
                    <Button variant="outline" size="sm" class="gap-1.5" @click="addPivotRow"><Plus class="w-3.5 h-3.5" /> Add Row Field</Button>
                </div>
                <div v-for="(r, i) in modelValue.pivot.rows" :key="i" class="flex items-center gap-2 border rounded-md p-2">
                    <select :value="r.table" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setPivotRow(i, { table: $event.target.value, column: '' })">
                        <option v-for="t in tables" :key="t.alias" :value="t.alias">{{ t.alias }}</option>
                    </select>
                    <select :value="r.column" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setPivotRow(i, { column: $event.target.value })">
                        <option value="" disabled>column</option>
                        <option v-for="c in columnsFor(r.table)" :key="c.columnName" :value="c.columnName">{{ c.columnName }}</option>
                    </select>
                    <Button variant="ghost" size="icon" class="h-7 w-7 text-destructive shrink-0 ml-auto" @click="removePivotRow(i)"><X class="w-3.5 h-3.5" /></Button>
                </div>
            </div>

            <div class="space-y-1.5">
                <Label>Column field (its values become table columns)</Label>
                <div class="flex items-center gap-2 border rounded-md p-2">
                    <select :value="modelValue.pivot.column.table" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setPivotColumn({ table: $event.target.value, column: '' })">
                        <option v-for="t in tables" :key="t.alias" :value="t.alias">{{ t.alias }}</option>
                    </select>
                    <select :value="modelValue.pivot.column.column" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setPivotColumn({ column: $event.target.value })">
                        <option value="" disabled>column</option>
                        <option v-for="c in columnsFor(modelValue.pivot.column.table)" :key="c.columnName" :value="c.columnName">{{ c.columnName }}</option>
                    </select>
                </div>
            </div>

            <div class="space-y-1.5">
                <Label>Value field (aggregated into each cell)</Label>
                <div class="flex items-center gap-2 border rounded-md p-2">
                    <select :value="modelValue.pivot.value.table" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setPivotValue({ table: $event.target.value, column: '' })">
                        <option v-for="t in tables" :key="t.alias" :value="t.alias">{{ t.alias }}</option>
                    </select>
                    <select :value="modelValue.pivot.value.column" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setPivotValue({ column: $event.target.value })">
                        <option value="" disabled>column</option>
                        <option v-for="c in columnsFor(modelValue.pivot.value.table)" :key="c.columnName" :value="c.columnName">{{ c.columnName }}</option>
                    </select>
                    <select :value="modelValue.pivot.value.aggregate" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setPivotValue({ aggregate: $event.target.value })">
                        <option v-for="a in AGGREGATES.filter((x) => x.value)" :key="a.value" :value="a.value">{{ a.label }}</option>
                    </select>
                </div>
            </div>
        </div>

        <!-- Filters + limit (shared by both modes) -->
        <div class="space-y-2 pt-2 border-t">
            <div class="flex items-center justify-between pt-2">
                <Label>Filters (WHERE)</Label>
                <Button variant="outline" size="sm" class="gap-1.5" @click="addFilter"><Plus class="w-3.5 h-3.5" /> Add Filter</Button>
            </div>
            <div v-for="(f, i) in modelValue.filters" :key="i" class="flex items-center gap-2 border rounded-md p-2 flex-wrap">
                <select :value="f.table" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setFilter(i, { table: $event.target.value, column: '' })">
                    <option v-for="t in tables" :key="t.alias" :value="t.alias">{{ t.alias }}</option>
                </select>
                <select :value="f.column" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setFilter(i, { column: $event.target.value })">
                    <option value="" disabled>column</option>
                    <option v-for="c in columnsFor(f.table)" :key="c.columnName" :value="c.columnName">{{ c.columnName }}</option>
                </select>
                <select :value="f.op" class="border rounded-md px-2 py-1 text-xs bg-background" @change="setFilter(i, { op: $event.target.value })">
                    <option v-for="op in OPERATORS" :key="op" :value="op">{{ op }}</option>
                </select>
                <Input :model-value="f.value" placeholder="value" class="h-7 text-xs w-32" @update:model-value="(v) => setFilter(i, { value: v })" />
                <Button variant="ghost" size="icon" class="h-7 w-7 text-destructive shrink-0 ml-auto" @click="removeFilter(i)"><X class="w-3.5 h-3.5" /></Button>
            </div>

            <div class="flex items-center gap-2 pt-2">
                <Label class="shrink-0">Limit</Label>
                <Input
                    type="number"
                    :model-value="modelValue.limit"
                    class="h-7 text-xs w-24"
                    @update:model-value="(v) => update({ limit: Number(v) || 1000 })"
                />
            </div>
        </div>
    </div>
</template>
