// -------------------------------------------------------------------------
// VoxelDataStore – pure static index-math helpers for the flat-array voxel
// data layout used across the terrain pipeline.  Methods hold no state and
// perform no allocation; callers supply grid dimensions as parameters.
//
// Array layout conventions:
//   density samples : x + totalSamplesX * (y + totalSamplesY * z)
//   cell materials  : x + totalCellsX   * (y + totalCellsY   * z)
//   surface prepass : sampleZ * totalSamplesX + sampleX
//   column prepass  : cellZ   * totalCellsX   + cellX
// -------------------------------------------------------------------------
internal static class VoxelDataStore
{
    public static int GetSampleIndex(int x, int y, int z, int totalSamplesX, int totalSamplesY)
        => x + totalSamplesX * (y + totalSamplesY * z);

    public static int GetCellIndex(int x, int y, int z, int totalCellsX, int totalCellsY)
        => x + totalCellsX * (y + totalCellsY * z);

    public static void GetCellCoordinates(int cellIndex, int totalCellsX, int totalCellsY, out int x, out int y, out int z)
    {
        z = cellIndex / (totalCellsX * totalCellsY);
        int remainder = cellIndex - (z * totalCellsX * totalCellsY);
        y = remainder / totalCellsX;
        x = remainder % totalCellsX;
    }

    public static int GetSurfacePrepassIndex(int sampleX, int sampleZ, int totalSamplesX)
        => (sampleZ * totalSamplesX) + sampleX;

    public static int GetColumnPrepassIndex(int cellX, int cellZ, int totalCellsX)
        => (cellZ * totalCellsX) + cellX;
}
