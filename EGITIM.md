# Koca Kafa — Model Eğitimi Rehberi

## Önemli gerçek

ChatGPT boyutunda bir modeli **tamamen sıfırdan** eğitmek ev ortamında mümkün değildir.
Ama Koca Kafa için **gerçek eğitim** şudur:

1. Onunla konuşursun (uygulama her cevabı kaydeder)
2. Yeterli veri birikince **LoRA fine-tune** yaparsın
3. Eğitilmiş modeli Ollama'ya yüklersin
4. Artık cevapları daha çok **senin tarzında** olur

Bu, çocuğunu büyütmek gibidir: genler (temel model) hazır gelir, kişilik senin verinle şekillenir.

---

## 1. Kurulum

### Ollama
1. https://ollama.com adresinden Ollama kur
2. Terminalde:
   ```
   ollama pull qwen2.5:3b
   ```
   Türkçe için iyi, hafif bir model.

### Koca Kafa uygulaması
Visual Studio ile `Koca_Kafa.slnx` aç ve çalıştır (F5).

---

## 2. Büyütme (günlük kullanım)

- Her gün Koca Kafa ile Türkçe konuş
- Ona şeyler öğret: "Benim adım Ali", "Kısa cevap ver", "Kolera bir sürücü kursu programıdır"
- Uygulama otomatik olarak eğitim verisini şuraya yazar:
  `%AppData%\Koca_Kafa\training\koca_kafa_dataset.jsonl`

**Hedef:** en az **50–100** kaliteli soru-cevap çifti (daha fazlası daha iyi).

---

## 3. Eğitim verisini dışa aktar

Uygulamada: **Dosya → Eğitim verisi dışa aktar**

---

## 4. LoRA eğitimi (gerçek model eğitimi)

Python 3.10+ ve NVIDIA GPU önerilir (8 GB+ VRAM).

```powershell
cd D:\Koca_Kafa\egitim
pip install torch transformers datasets peft trl bitsandbytes accelerate
python train_lora.py --dataset C:\yol\koca_kafa_dataset.jsonl --output .\koca_kafa_lora
```

Bu işlem temel modelin ağırlıklarını senin verinle günceller (ince ayar).

---

## 5. Ollama'ya yükleme

Eğitim sonrası model Hugging Face formatında olur. Ollama'ya almak için:

1. Modeli GGUF formatına çevir (llama.cpp `convert_hf_to_gguf.py`)
2. Bir `Modelfile` oluştur:

```
FROM ./koca_kafa.gguf
SYSTEM Sen Koca Kafa'sın. Türkçe konuş.
```

3. Terminalde:
```
ollama create koca-kafa -f Modelfile
```

4. Uygulama ayarlarında model adını `koca-kafa` yap.

---

## Büyüme takvimi (öneri)

| Aşama | Ne yaparsın | Sonuç |
|-------|-------------|-------|
| Doğum | Ollama + qwen2.5:3b | İlk konuşmalar |
| 1. ay | Günlük sohbet, 50+ örnek | Veri birikir |
| 2. ay | LoRA eğitimi | İlk "benim" model |
| 3. ay+ | Yeni veri + tekrar eğitim | Daha da senin gibi |

---

## Sık sorulan

**API kullanıyor mu?** Hayır. Her şey yerel (Ollama).

**İnternet gerekli mi?** Sadece ilk kurulumda (Ollama + model indirme).

**GPU şart mı?** Sohbet için hayır. Eğitim için evet (yoksa Google Colab kullanılabilir).
