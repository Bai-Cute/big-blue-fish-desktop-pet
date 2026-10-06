"""Import the reviewed DSH Pet pack and freeze its 0.3.0 scheduling rules."""
import json, shutil, sys
from pathlib import Path

source, target = map(Path, sys.argv[1:3])
notes = json.loads((source / '动画使用批示.json').read_text(encoding='utf-8-sig'))['annotations']
target.mkdir(parents=True, exist_ok=True)
items = {n['id']: dict(Id=n['id'], File=n['id']+'.webm', Pool='Event', Weight=100) for n in notes.values()}
def setrule(names, **rules):
    for name in names.split('|'): items[name].update(rules)
setrule('待机呼吸休闲', Pool='Idle')
setrule('悠闲哼歌', Pool='Idle', Weight=20)
setrule('原地小憩沉眠', Pool='Idle', Weight=40, NightOnly=True)
setrule('余额-分文不剩|余额-数金皱眉|余额-袋空如洗|工作状态-垂头叹气冒汗', Pool='Disabled', Weight=0)
setrule('女仆屈膝礼仪', Pool='Greeting')
setrule('晨间刷牙', Pool='Daily')
setrule('被鼠标拖拽悬空反馈', Pool='Drag')
setrule('点击回应-元气挥手|点击回应-害羞惊讶', Pool='Click')
setrule('点击回应-傲娇生气|点击回应-开心跃动|点击回应-挠痒咯咯笑', Pool='Click', Weight=20)
setrule('东张西望|原地漂浮踏步|吃白饭|工作状态-原地踱步张望|轻快摇摆舞', Weight=1000)
setrule('碎碎念-对屏碎碎念', Weight=500)
setrule('优雅女仆舞', Weight=300)
setrule('鲸鱼吐泡泡特效', Weight=200)
setrule('余额-钱袋满溢|动物环绕|吃糖葫芦|大口吃零食|工作状态-雀跃庆祝|是啊，吃什么|蝴蝶蜜蜂环绕头顶开花|被吓一跳', Weight=30)
setrule('写代码', WorkKind='Coding', WorkWeight=1000)
setrule('工作状态-忙碌点按|工作状态-思考冒泡|工作状态-清点归档', WorkKind='Working', WorkWeight=1000)
setrule('吃Token|吃大闸蟹|吃年糕|吃重阳糕|吃长寿面|吃青团|吃饺子|涮火锅', Meal='Any', MealBoost=400)
setrule('吃早餐', Meal='Breakfast', MealBoost=900)
setrule('吃晚餐', Meal='Lunch', MealBoost=900)
setrule('吃午餐', Meal='Dinner', MealBoost=900)
setrule('吃冰淇淋融化', SeasonOnly='Summer', Meal='Any', MealBoost=400)
setrule('吃西瓜', SummerWeight=300)
for name, festival, base in [('吃汤圆','Lantern',30),('吃粽子','DragonBoat',30),('吃腊八粥','Laba',100)]:
    setrule(name, Weight=base, Festival=festival, RadiusDays=7, FestivalMode='Window', FestivalWeight=100, Meal='Any', MealBoost=2400)
setrule('哈欠连天', MorningWeight=500)
setrule('堆雪人', SeasonOnly='Winter', Weight=300)
setrule('摇扇纳凉', WinterWeight=0, SummerWeight=300)
setrule('被落叶淹没', SeasonOnly='Autumn')
for name, festival, radius, base in [('中秋赏月吃月饼','MidAutumn',7,0),('写福字','SpringFestival',14,0),('收红包','SpringFestival',14,0),('舞狮头','SpringFestival',7,0),('放烟花','SpringFestival',14,30),('穿针乞巧','Qixi',7,0),('装点圣诞树','Christmas',14,0),('讨糖南瓜灯','Halloween',14,0)]:
    setrule(name, Weight=base, Festival=festival, RadiusDays=radius, FestivalMode='Triangle', FestivalWeight=1000)
setrule('凭空生花', Festival='ValentineOrMay20', RadiusDays=7, FestivalMode='Triangle', FestivalWeight=1000)
setrule('原地左转奔跑', Weight=200, MoveKind='Run', HourParity=1)
setrule('螃蟹走路', Weight=200, MoveKind='Crab', HourParity=0)
for item in items.values():
    shutil.copyfile(source/'assets'/'p'/item['File'], target/item['File'])
(target/'animations.json').write_text(json.dumps(list(items.values()), ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
print(f'Imported {len(items)} reviewed animation rules')
