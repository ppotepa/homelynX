FROM python:3.12-slim

WORKDIR /app
ENV PYTHONUNBUFFERED=1

COPY requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY app.py .
COPY coord_input_app ./coord_input_app

EXPOSE 5070
CMD ["uvicorn", "app:app", "--host", "0.0.0.0", "--port", "5070"]
