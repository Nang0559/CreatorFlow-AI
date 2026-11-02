FROM python:3.11-slim
ENV PYTHONDONTWRITEBYTECODE=1 PYTHONUNBUFFERED=1
WORKDIR /app
COPY ai/pyproject.toml ./pyproject.toml
COPY ai/creatorflow_ai ./creatorflow_ai
RUN pip install --no-cache-dir . \
    && useradd --create-home --uid 10001 appuser \
    && chown -R appuser:appuser /app
USER appuser
EXPOSE 8000
CMD ["uvicorn", "creatorflow_ai.main:app", "--host", "0.0.0.0", "--port", "8000"]
