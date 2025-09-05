namespace Data.Building
{
	// "이 부품 전체는 무엇인가?" (프리셋용)
	public enum BuildingPartType
	{
		Generic, // 일반 (수동 설정)
		Floor, // 바닥
		Wall, // 벽
		Window,
		Foundation, // 토대
		Ceiling, // 천장
		Roof, // 지붕
		Door,
		
	}

	// "이 연결점의 규격은 무엇인가?" (실제 스냅 계산용)
	public enum SnapType
	{
		None,
		Foundation,
		Floor,
		Wall,
		Ceiling,
		Roof,
		Socket,
		Pillar,
		Door,
	}
}