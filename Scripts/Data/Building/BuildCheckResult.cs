namespace Data.Building
{
	public enum BuildCheckResult
	{
		Success, // 성공
		Overlapping, // 다른 물체와 겹침
		BadSlope, // 경사각 문제
		InvalidSurface // 지지하는 표면이 없음
	}
}