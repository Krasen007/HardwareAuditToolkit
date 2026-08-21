llama serve -hf jica98/qwen3.5-4B-super-coder:Q4_0 -fa on --no-mmproj --port 8000 -c 262144 --temp 0.6 --top-p 0.95 --top-k 20 --min-p 0.0 --presence-penalty 0.0 --repeat-penalty 1.0
pause

llama serve -hf ornith-ai/Ornith-1.5-9B-GGUF:Q4_K_M --port 8000 -c 262144 --temp 0.6 --top-p 0.95 --top-k 20 --min-p 0.0 --presence-penalty 0.0 --repeat-penalty 1.0
pause