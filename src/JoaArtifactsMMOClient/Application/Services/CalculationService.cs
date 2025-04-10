public static class CalculationService {
    public static int CalculateDistanceToMap(int originX, int originY, int mapX, int mapY) {
        
        return Math.Abs((mapX - originX) + mapY - originY);
    }
}