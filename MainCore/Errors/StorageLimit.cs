namespace MainCore.Errors
{
    public class StorageLimit : Error
    {
        protected StorageLimit(BuildingEnums building, string type, long storage, long required) : base($"{type} doesn't have enough capacity, need {required} but have {storage} ({required - storage})")
        {
            Building = building;
        }

        // Which storage building has to be enlarged to satisfy this error (Warehouse / Granary).
        public BuildingEnums Building { get; }

        public static StorageLimit Warehouse(long storage, long required) => new(BuildingEnums.Warehouse, "Warehouse", storage, required);

        public static StorageLimit Granary(long storage, long required) => new(BuildingEnums.Granary, "Granary", storage, required);
    }
}