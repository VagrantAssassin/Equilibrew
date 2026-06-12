from openai import OpenAI

endpoint = "http://localhost:20128/v1"
deployment_name = "azure/gpt-5.4"
token_provider = "sk-473fedd5bd789e1e-49rflf-805a0b0a"

client = OpenAI(
    base_url=endpoint,
    api_key=token_provider
)

completion = client.chat.completions.create(
    model=deployment_name,
    messages=[
        {
            "role": "user",
            "content": "apakah paris ibu kota indonesia?",
        }
    ],
)

print(completion.choices[0].message)