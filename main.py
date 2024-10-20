import random
from TTS.api import TTS

voices: dict[str, str] = {
    "p226": "Male 01",
    "p228": "Male 02",
    "p229": "Male 03",
    "p230": "Male 04",
    "p231": "Male 05",
    "p232": "Male 06",
    "p233": "Male 07",
    "p234": "Male 08",
    "p236": "Male 09",
    "p238": "Male 10",
    "p239": "Male 11",
    "p241": "Male 12",
    "p251": "Male 13",
    "p252": "Male 14",
    "p253": "Male 15",
    "p254": "Male 16",
    "p255": "Male 17",
    "p256": "Male 18",
    "p257": "Male 19",
    "p258": "Male 20",
    "p262": "Male 21",
    "p264": "Male 22",
    "p265": "Male 23",
    "p266": "Male 24",
    "p267": "Male 25",
    "p269": "Male 26",
    "p272": "Male 27",
    "p279": "Male 28",
    "p281": "Male 29",
    "p285": "Male 30",
    "p286": "Male 31",
    "p287": "Male 32",
    "p298": "Male 33",
    "p225": "Female 01",
    "p227": "Female 02",
    "p237": "Female 03",
    "p240": "Female 04",
    "p243": "Female 05",
    "p244": "Female 06",
    "p245": "Female 07",
    "p246": "Female 08",
    "p247": "Female 09",
    "p248": "Female 10",
    "p249": "Female 11",
    "p250": "Female 12",
    "p259": "Female 13",
    "p260": "Female 14",
    "p261": "Female 15",
    "p263": "Female 16",
    "p270": "Female 17",
    "p271": "Female 18",
    "p273": "Female 19",
    "p274": "Female 20",
    "p275": "Female 21",
    "p276": "Female 22",
    "p277": "Female 23",
    "p278": "Female 24",
    "p280": "Female 25",
    "p283": "Female 26",
    "p284": "Female 27",
    "p288": "Female 28",
    "p293": "Female 29",
    "p294": "Female 30",
    "p295": "Female 31",
    "p297": "Female 32"
}

tts = TTS("tts_models/en/vctk/vits", progress_bar=False, gpu=False)
voice_list: list[str] = [key for key in voices.keys()]
code = random.choice(voice_list)
voice = voices[code]
text = f"Hello, I'm voice {voice} from coquis TTS."


tts.tts_to_file(text=text, speaker=code, file_path=f"test_{voice}.wav")
