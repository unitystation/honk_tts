from TTS.api import TTS

tts = TTS("tts_models/en/vctk/vits", progress_bar=False, gpu=False)
text = "Hello, how are you? I'm underwater. Please help me!"
tts.tts_to_file(text=text, speaker="p243", file_path="wea.wav")

