"""Captain admin/staff live journey against the real local stack (API + MySQL + Mailpit).

Prerequisite: tools/captain/bootstrap-admin.sh once, then restart-stack.sh (operator bootstrap).
Synthetic accounts only; tokens, passwords and invitation codes are never printed.
Usage: python3 smoke_admin.py [base_url]
"""
import json
import os
import re
import sys
import time
import urllib.request
import uuid

import smoke_journey as j
from smoke_journey import call, check, code_of

MAILPIT = "http://127.0.0.1:8025/api/v1"
SECRETS = os.path.expanduser("~/bm-captain/secrets")


def auth(method, path, body=None, token=None):
    """Auth endpoints share a strict limiter; back off once on 429 like a well-behaved client."""
    status, payload, headers = call(method, path, body, token=token)
    if status == 429:
        time.sleep(61)
        status, payload, headers = call(method, path, body, token=token)
    return status, payload, headers


def login(email, password):
    status, body, _ = auth("POST", "/api/v1/auth/login", {"email": email, "password": password})
    return status, (body or {}).get("accessToken") if isinstance(body, dict) else None


def mail_code(recipient, subject_word, timeout=30):
    """Reads the newest captured message for the recipient from Mailpit and extracts the 64-hex code."""
    deadline = time.time() + timeout
    while time.time() < deadline:
        with urllib.request.urlopen(f"{MAILPIT}/search?query=to:{urllib.request.quote(recipient)}", timeout=10) as r:
            messages = json.loads(r.read()).get("messages", [])
        messages = [m for m in messages if subject_word in m.get("Subject", "")]
        if messages:
            with urllib.request.urlopen(f"{MAILPIT}/message/{messages[0]['ID']}", timeout=10) as r:
                text = json.loads(r.read()).get("Text", "")
            match = re.search(r"\b[0-9a-fA-F]{64}\b", text)
            if match:
                return match.group(0)
        time.sleep(1)
    return None


def main():
    if len(sys.argv) > 1 and sys.argv[1].startswith("http"):
        j.BASE = sys.argv[1]
    admin_email = open(os.path.join(SECRETS, "captain-admin.email")).read().strip()
    admin_password = open(os.path.join(SECRETS, "captain-admin.password")).read().strip()

    status, admin = login(admin_email, admin_password)
    check("bootstrapped admin logs in", status == 200 and admin, status)
    status, dash, _ = call("GET", "/api/v1/admin/dashboard", token=admin)
    check("admin dashboard (200) with operational statistics", status == 200 and "projects" in dash and "pendingProposals" in dash, dash)
    status, _, _ = call("GET", "/api/v1/admin/dashboard")
    check("anonymous admin access rejected (401)", status == 401, status)

    customer = j.new_user("customer")
    status, body, _ = call("GET", "/api/v1/admin/dashboard", token=customer["access"])
    check("customer cannot reach admin APIs (403 PERMISSION_DENIED)", status == 403 and code_of(body) == "PERMISSION_DENIED", body)
    status, _, _ = call("GET", "/api/v1/staff/dashboard", token=customer["access"])
    check("customer cannot reach staff APIs (403)", status == 403, status)
    status, project, _ = call("POST", "/api/v1/projects", {"name": "Customer synthetic project", "projectType": "Residential"},
                              token=customer["access"])
    pid = project["id"]

    # Staff invitation: no temporary password; a single-use code is delivered by email (local capture only).
    staff_email = f"captain-staff-{uuid.uuid4().hex[:8]}@example.invalid"
    status, body, _ = call("POST", "/api/v1/admin/staff", {"email": staff_email, "fullName": "Synthetic Staff", "roles": ["ADMIN"],
                                                          "modules": ["SURVEYS"]}, token=admin)
    check("mass assignment of roles on staff creation rejected (400)", status == 400, body)
    status, invite, _ = call("POST", "/api/v1/admin/staff", {"email": staff_email, "fullName": "Synthetic Staff",
                                                            "modules": ["SURVEYS", "SYSTEM_POLICIES"]}, token=admin)
    check("admin invites staff (201, invitation sent)", status == 201 and invite.get("invitationSent") is True, invite)
    check("invitation code never appears in the API response", not re.search(r"[0-9a-f]{64}", json.dumps(invite)))
    staff_id = invite.get("userId")
    status, _ = login(staff_email, "Not-the-password-123!")
    check("invited staff cannot log in before setting a password (401)", status == 401, status)
    code = mail_code(staff_email, "invitation")
    check("invitation email captured locally with a set-password code", code is not None)
    staff_password = "Staff-" + uuid.uuid4().hex[:16] + "9a!"
    status, _, _ = auth("POST", "/api/v1/auth/reset-password", {"email": staff_email, "token": code, "newPassword": staff_password, "confirmPassword": staff_password})
    check("staff sets password with the single-use code (204)", status == 204, status)
    status, _, _ = auth("POST", "/api/v1/auth/reset-password", {"email": staff_email, "token": code, "newPassword": staff_password + "x", "confirmPassword": staff_password + "x"})
    check("invitation code cannot be reused (400)", status == 400, status)
    status, staff = login(staff_email, staff_password)
    check("staff logs in", status == 200 and staff, status)

    status, sdash, _ = call("GET", "/api/v1/staff/dashboard", token=staff)
    check("staff dashboard lists assigned modules", status == 200 and sdash.get("modules") == ["SURVEYS", "SYSTEM_POLICIES"], sdash)
    status, _, _ = call("GET", "/api/v1/admin/dashboard", token=staff)
    check("staff cannot reach admin-only APIs (403)", status == 403, status)
    status, surveys, _ = call("GET", "/api/v1/staff/monitoring/surveys", token=staff)
    check("staff views an assigned module (surveys monitoring)", status == 200 and "items" in surveys, status)
    status, _, _ = call("GET", "/api/v1/staff/monitoring/reports", token=staff)
    check("staff denied an unassigned module (403)", status == 403, status)

    # Proposal queue: staff writes are pending until admin approval.
    key = "captain.smoke." + uuid.uuid4().hex[:8]
    status, body, _ = call("POST", "/api/v1/staff/proposals", {"module": "REPORTS", "targetType": "SYSTEM_POLICY", "targetKey": key,
                                                              "changeType": "CREATE", "payload": {"x": 1}}, token=staff)
    check("staff proposal in an unassigned module denied (403)", status == 403, body)
    status, proposal, _ = call("POST", "/api/v1/staff/proposals", {"module": "SYSTEM_POLICIES", "targetType": "SYSTEM_POLICY", "targetKey": key,
                                                                  "changeType": "CREATE", "payload": {"days": 30}}, token=staff)
    check("staff proposal recorded as PENDING (201)", status == 201 and proposal.get("status") == "PENDING", proposal)
    status, policies, _ = call("GET", "/api/v1/admin/policies", token=admin)
    check("pending proposal does not change live policy", all(p["key"] != key for p in policies.get("policies", [])), policies)
    status, _, _ = call("POST", f"/api/v1/admin/proposals/{proposal['proposalId']}/approve", {"expectedRowVersion": 1}, token=staff)
    check("staff cannot approve proposals (403)", status == 403, status)
    status, approved, _ = call("POST", f"/api/v1/admin/proposals/{proposal['proposalId']}/approve", {"expectedRowVersion": 1}, token=admin)
    check("admin approval applies the change (APPLIED)", status == 200 and approved.get("status") == "APPLIED", approved)
    status, policies, _ = call("GET", "/api/v1/staff/policies", token=staff)
    check("approved policy visible to staff with the module", any(p["key"] == key for p in policies.get("policies", [])), policies)
    u1 = call("POST", "/api/v1/staff/proposals", {"module": "SYSTEM_POLICIES", "targetType": "SYSTEM_POLICY", "targetKey": key,
                                                  "changeType": "UPDATE", "baseVersion": 1, "payload": {"days": 60}}, token=staff)[1]
    u2 = call("POST", "/api/v1/staff/proposals", {"module": "SYSTEM_POLICIES", "targetType": "SYSTEM_POLICY", "targetKey": key,
                                                  "changeType": "UPDATE", "baseVersion": 1, "payload": {"days": 90}}, token=staff)[1]
    call("POST", f"/api/v1/admin/proposals/{u1['proposalId']}/approve", {"expectedRowVersion": 1}, token=admin)
    status, body, _ = call("POST", f"/api/v1/admin/proposals/{u2['proposalId']}/approve", {"expectedRowVersion": 1}, token=admin)
    check("proposal against a superseded base refused (409 STALE_PROPOSAL)", status == 409 and code_of(body) == "STALE_PROPOSAL", body)

    # Tasks and purpose-bound project review.
    status, task, _ = call("POST", "/api/v1/admin/tasks", {"assigneeId": staff_id, "title": "Review survey processing", "module": "SURVEYS",
                                                          "projectId": pid}, token=admin)
    check("admin assigns a task (201)", status == 201 and task.get("status") == "OPEN", task)
    tid = task["taskId"]
    status, _, _ = call("POST", f"/api/v1/staff/tasks/{tid}/project-review", {"purpose": "short"}, token=staff)
    check("project review without a real purpose refused (422)", status == 422, status)
    status, review, _ = call("POST", f"/api/v1/staff/tasks/{tid}/project-review", {"purpose": "Check survey processing status for the assigned task"},
                             token=staff)
    check("assigned staff gets metadata-only project review", status == 200 and review.get("projectId") == pid and "surveys" in review, review)
    status, _, _ = call("POST", f"/api/v1/staff/tasks/{tid}/project-review", {"purpose": "Customer attempting staff review path"},
                        token=customer["access"])
    check("customer cannot use staff review (403)", status == 403, status)
    status, _, _ = call("GET", f"/api/v1/projects/{pid}", token=staff)
    check("staff role does not bypass project ownership (404)", status == 404, status)
    status, _, _ = call("POST", f"/api/v1/staff/tasks/{tid}/status", {"status": "CANCELLED", "expectedVersion": 1}, token=staff)
    check("staff cannot cancel tasks (403)", status == 403, status)
    status, moved, _ = call("POST", f"/api/v1/staff/tasks/{tid}/status", {"status": "ESCALATED", "expectedVersion": 1,
                                                                         "note": "Needs admin decision"}, token=staff)
    check("staff escalates the task with a reason", status == 200 and moved.get("status") == "ESCALATED", moved)
    status, detail, _ = call("GET", f"/api/v1/admin/tasks/{tid}", token=admin)
    check("admin sees the escalation note", status == 200 and any(n["kind"] == "ESCALATION" for n in detail.get("notes", [])), detail)

    # Privilege changes take effect immediately and revoke sessions.
    status, staff_list, _ = call("GET", "/api/v1/admin/staff?search=" + staff_email.split("@")[0], token=admin)
    scope_version = next(s["scopeVersion"] for s in staff_list["items"] if s["userId"] == staff_id)
    status, _, _ = call("PUT", f"/api/v1/admin/staff/{staff_id}/modules", {"modules": ["REPORTS"], "expectedScopeVersion": scope_version + 5},
                        token=admin)
    check("stale scope version refused (409)", status == 409, status)
    status, _, _ = call("PUT", f"/api/v1/admin/staff/{staff_id}/modules", {"modules": ["REPORTS"], "expectedScopeVersion": scope_version},
                        token=admin)
    check("admin changes staff modules", status == 200, status)
    status, _, _ = call("GET", "/api/v1/staff/dashboard", token=staff)
    check("existing staff session revoked after scope change (401)", status == 401, status)
    status, staff = login(staff_email, staff_password)
    status, _, _ = call("GET", "/api/v1/staff/monitoring/surveys", token=staff)
    check("removed module no longer accessible (403)", status == 403, status)
    status, body, _ = call("POST", f"/api/v1/admin/accounts/{staff_id}/suspend", {"reason": "Synthetic suspension test"}, token=admin)
    check("admin suspends the staff account", status == 200 and body.get("isActive") is False, body)
    status, _, _ = call("GET", "/api/v1/staff/dashboard", token=staff)
    check("suspended staff session rejected (401)", status == 401, status)
    status, _ = login(staff_email, staff_password)
    check("suspended staff cannot log in", status in (401, 403), status)
    status, body, _ = call("POST", f"/api/v1/admin/accounts/{staff_id}/activate", {"reason": "Synthetic reactivation"}, token=admin)
    check("admin reactivates the account", status == 200 and body.get("isActive") is True, body)

    # Roles: no self-escalation; grants revoke the target's sessions.
    me = call("GET", "/api/v1/users/me", token=admin)[1]
    status, body, _ = call("POST", f"/api/v1/admin/accounts/{me.get('id') or me.get('userId')}/roles", {"role": "ADMIN", "granted": False}, token=admin)
    check("admin cannot change own roles (409)", status == 409, body)
    customer_id = next(a["userId"] for a in call("GET", "/api/v1/admin/customers?search=" + customer["email"].split("@")[0], token=admin)[1]["items"])
    status, body, _ = call("POST", f"/api/v1/admin/accounts/{customer_id}/roles", {"role": "ARCHITECT", "granted": True}, token=admin)
    check("admin grants the architect role", status == 200 and body.get("changed") is True, body)
    status, _, _ = call("GET", "/api/v1/projects", token=customer["access"])
    check("role grant revokes the target's existing session (401)", status == 401, status)

    # Password recovery delivered through the local mail capture.
    status, _, _ = auth("POST", "/api/v1/auth/forgot-password", {"email": customer["email"]})
    check("forgot-password accepted (202, generic response)", status == 202, status)
    code = mail_code(customer["email"], "password reset")
    check("reset email captured locally", code is not None)
    new_password = "Reset-" + uuid.uuid4().hex[:16] + "9a!"
    status, _, _ = auth("POST", "/api/v1/auth/reset-password", {"email": customer["email"], "token": code, "newPassword": new_password, "confirmPassword": new_password})
    check("password reset with the emailed code (204)", status == 204, status)
    status, _ = login(customer["email"], customer["password"])
    check("old password rejected after reset (401)", status == 401, status)
    status, _ = login(customer["email"], new_password)
    check("new password accepted", status == 200, status)
    status, _, _ = auth("POST", "/api/v1/auth/forgot-password", {"email": "nobody-" + uuid.uuid4().hex[:6] + "@example.invalid"})
    check("unknown email gets the same generic response (no enumeration)", status == 202, status)

    status, audit, _ = call("GET", "/api/v1/admin/audit?pageSize=100", token=admin)
    actions = {e["action"] for e in audit.get("items", [])}
    check("audit trail records staff, proposal, task, review and suspension events",
          {"STAFF_CREATED", "PROPOSAL_APPLIED", "PROPOSAL_STALE", "TASK_CREATED", "PROJECT_REVIEW_ACCESS", "ACCOUNT_SUSPENDED",
           "STAFF_SCOPE_CHANGED", "ROLE_GRANTED"} <= actions, sorted(actions))
    status, verify, _ = call("GET", "/api/v1/admin/audit/verify", token=admin)
    check("audit hash chain verifies intact", status == 200 and verify.get("intact") is True, verify)
    check("no audit event contains a set-password or reset code", not re.search(r"[0-9a-f]{64}\"", json.dumps(
        [e["detailsJson"] for e in audit.get("items", [])])))

    failed = [name for name, ok in j.results if not ok]
    print(f"\nSUMMARY: {len(j.results) - len(failed)} passed, {len(failed)} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
