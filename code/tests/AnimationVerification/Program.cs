using VPet_Simulator.Windows;

var directory = args.Length == 0 ? Path.GetFullPath("code/VPet-Simulator.Windows/assets/fish") : args[0];
var policy = CompanionAnimationPolicy.Load(directory);
int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
void Weight(string name, DateTime time, double expected, CompanionAnimationContext? context = null)
    => Check(Math.Abs(policy.Weight(policy.Find(name), context ?? new(time)) - expected) < .00001, $"{name} at {time}: expected {expected}");
var daytime = new DateTime(2026, 10, 7, 15, 30, 0);
Check(policy.Rules.Count == 106, "106 animations");
foreach (var rule in policy.Rules)
{
    Check(File.Exists(Path.Combine(directory, rule.File)), "missing " + rule.File);
    Check(rule.Pool is "Idle" or "Event" or "Click" or "Greeting" or "Daily" or "Drag" or "Disabled", "pool " + rule.Id);
}
Check(policy.Rules.Count(r => r.Pool == "Idle") == 3, "idle coverage");
Check(policy.Rules.Count(r => r.Pool == "Click") == 5, "click coverage");
Check(policy.Rules.Count(r => r.Pool == "Disabled") == 4, "disabled coverage");
Weight("待机呼吸休闲", daytime, 100); Weight("悠闲哼歌", daytime, 20);
Weight("原地小憩沉眠", daytime, 0); Weight("原地小憩沉眠", daytime.Date.AddHours(23), 40);
Weight("原地小憩沉眠", daytime.Date.AddHours(7.99), 40); Weight("原地小憩沉眠", daytime.Date.AddHours(8), 0);
foreach (var rule in policy.Rules.Where(r => r.Pool == "Disabled")) Weight(rule.Id, daytime, 0);
Weight("写代码", daytime, 100, new(daytime, false, true, true));
Weight("写代码", daytime, 1000, new(daytime, true, true, true));
foreach (var name in new[]{"工作状态-忙碌点按","工作状态-思考冒泡","工作状态-清点归档"})
{ Weight(name, daytime, 100, new(daytime, false, true, true)); Weight(name, daytime, 1000, new(daytime, true, true, false)); }
Weight("工作状态-原地踱步张望", daytime, 1000);
Weight("吃早餐", daytime.Date.AddHours(8), 1000);
Weight("吃晚餐", daytime.Date.AddHours(12), 1000); Weight("吃午餐", daytime.Date.AddHours(18), 1000);
Weight("吃午餐", daytime.Date.AddHours(12), 100); Weight("吃晚餐", daytime.Date.AddHours(18), 100);
foreach (var name in new[]{"吃Token","吃大闸蟹","吃年糕","吃重阳糕","吃长寿面","吃青团","吃饺子","涮火锅"})
{ Weight(name, daytime, 100); Weight(name, daytime.Date.AddHours(11), 500); Weight(name, daytime.Date.AddHours(11.5), 500); Weight(name, daytime.Date.AddHours(13), 500); Weight(name, daytime.Date.AddHours(13.01), 100); }
Weight("吃早餐", daytime.Date.AddHours(9), 1000); Weight("吃早餐", daytime.Date.AddHours(9.01), 100);
Weight("吃早餐", daytime, 1000, new(daytime, BreakfastHour: 15));
Weight("吃冰淇淋融化", daytime, 0); Weight("吃冰淇淋融化", new(2026,7,1,12,0,0), 500);
Weight("堆雪人", new(2026,12,1), 300); Weight("堆雪人", daytime, 0);
Weight("摇扇纳凉", new(2026,12,1), 0); Weight("摇扇纳凉", new(2026,7,1), 300); Weight("摇扇纳凉", daytime, 100);
Weight("被落叶淹没", daytime, 100); Weight("被落叶淹没", new(2026,7,1), 0);
Weight("哈欠连天", daytime.Date.AddHours(8), 500); Weight("哈欠连天", daytime.Date.AddHours(11), 100);
// Independently known lunar dates, including the 2025 leap sixth month.
Check(policy.FestivalDistance(new(2026,2,17), "SpringFestival") == 0, "2026 lunar new year");
Check(policy.FestivalDistance(new(2026,3,3), "Lantern") == 0, "2026 lantern");
Check(policy.FestivalDistance(new(2026,6,19), "DragonBoat") == 0, "2026 dragon boat");
Check(policy.FestivalDistance(new(2025,10,6), "MidAutumn") == 0, "leap month normalization");
Weight("写福字", new(2026,2,17), 1000); Weight("写福字", new(2026,2,10), 500); Weight("写福字", new(2026,2,3), 0);
Weight("放烟花", new(2026,2,17), 1000); Weight("放烟花", new(2026,2,10), 515); Weight("放烟花", daytime, 30);
Weight("凭空生花", new(2026,2,14), 1000); Weight("凭空生花", new(2026,5,20), 1000); Weight("凭空生花", daytime, 100);
Weight("装点圣诞树", new(2026,12,25), 1000); Weight("装点圣诞树", new(2027,1,1), 500);
Weight("吃汤圆", new(2026,3,3,12,0,0), 2500); Weight("吃汤圆", new(2026,3,10,12,0,0), 2500);
Weight("吃汤圆", new(2026,3,11,12,0,0), 30); Weight("吃汤圆", new(2026,3,3,15,0,0), 100);
Weight("吃粽子", new(2026,6,19,12,0,0), 2500); Weight("吃粽子", daytime, 30);
Weight("吃腊八粥", daytime.Date.AddHours(12), 100);
Weight("原地左转奔跑", new(2026,10,7,15,5,0), 200); Weight("原地左转奔跑", new(2026,10,7,15,55,0), 0);
Weight("螃蟹走路", new(2026,10,7,14,30,0), 200); Weight("原地左转奔跑", new(2026,10,7,14,30,0), 0);
var random = new Random(30);
var click = new Dictionary<string,int>();
for (int i=0;i<26000;i++) { var rule=policy.Choose("Click",new(daytime),random); click[rule.Id]=click.GetValueOrDefault(rule.Id)+1; }
foreach (var rule in policy.Rules.Where(r=>r.Pool=="Click")) Check(Math.Abs(click[rule.Id]/26000.0-rule.Weight/260.0)<.02,"click distribution "+rule.Id);
for(int i=0;i<1000;i++) { Check(policy.Choose("Idle",new(daytime),random).Id!="原地小憩沉眠","no daytime sleep"); Check(policy.Choose("Event",new(daytime),random).Pool=="Event","event isolation"); }
Check(policy.FestivalDistance(new(2200,1,1),"SpringFestival")==double.PositiveInfinity,"calendar range fallback");
Console.WriteLine($"Animation policy: {checks} checks passed.");
