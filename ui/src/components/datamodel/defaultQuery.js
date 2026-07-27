// Default shape for a DataModelQueryBuilder request, shared by the Data Models preview
// tab and the dashboard "Data Model" widget so both start from the same empty state.
export function createDefaultQuery() {
    return { fields: [], filters: [], orderBy: [], limit: 1000, pivot: null };
}
