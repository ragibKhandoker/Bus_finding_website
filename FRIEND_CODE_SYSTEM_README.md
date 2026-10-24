# Friend Code System

## Overview
This system replaces the old MongoDB ID-based friend search with a user-friendly 5-character friend code system.

## How It Works

### 1. **Automatic Friend Code Generation**
- When a user registers, a unique 5-character friend code is automatically generated
- Friend codes are stored in a new `friends` collection
- Codes are unique across all users

### 2. **Friend Code Format**
- **5 characters long**
- **Uppercase letters and numbers only** (A-Z, 0-9)
- **Examples**: `A1B2C`, `XYZ12`, `12345`

### 3. **Profile Display**
- User's friend code is displayed under their name in the profile
- **Old**: `id: 507f1f77bcf86cd799439011`
- **New**: `Friend Code: A1B2C`

### 4. **Friend Search**
- Users can search for friends using the 5-character code
- No more long MongoDB IDs to remember
- Clean, user-friendly interface

## Database Changes

### New Collection: `friends`
```javascript
{
  userId: ObjectId,        // Reference to User
  friendCode: String,      // 5-character unique code
  createdAt: Date          // When code was generated
}
```

### New API Endpoints
- `GET /auth/friend-code` - Get current user's friend code
- `GET /auth/search-friend/:friendCode` - Search user by friend code

## Setup Instructions

### 1. **For New Users**
- Friend codes are automatically generated during registration
- No additional setup required

### 2. **For Existing Users**
Run the migration script to generate friend codes for existing users:

```bash
cd server
node generateFriendCodes.js
```

This will:
- Connect to your MongoDB database
- Find all existing users
- Generate unique friend codes for each user
- Save them to the `friends` collection

### 3. **Verify Setup**
- Check that the `friends` collection exists in your database
- Verify that each user has a unique friend code
- Test the search functionality

## Usage

### **Finding Your Friend Code**
1. Go to Profile screen
2. Your friend code is displayed under your name
3. Share this code with friends

### **Adding Friends**
1. Go to Friends screen
2. Enter the 5-character friend code
3. Click Search
4. If found, the user's information will be displayed

### **Input Validation**
- Only allows A-Z and 0-9 characters
- Automatically converts to uppercase
- Limits input to exactly 5 characters
- Shows helper text for guidance

## Benefits

✅ **User-Friendly**: Easy to remember 5-character codes
✅ **Unique**: No duplicate codes across users
✅ **Automatic**: Codes generated during registration
✅ **Secure**: Still requires authentication
✅ **Clean UI**: Modern, intuitive interface

## Troubleshooting

### **Friend Code Not Showing**
- Check if user exists in `friends` collection
- Run migration script for existing users
- Verify authentication is working

### **Search Not Working**
- Ensure friend code is exactly 5 characters
- Check server logs for errors
- Verify the `friends` collection exists

### **Migration Issues**
- Check MongoDB connection
- Ensure models are properly imported
- Check for duplicate friend codes

## Testing

1. **Register a new user** - Should automatically get a friend code
2. **Check profile** - Friend code should be visible
3. **Search by friend code** - Should find the user
4. **Test invalid codes** - Should show "not found" message

## Security Notes

- Friend codes are public information
- Users can only search, not access private data
- All searches require valid JWT authentication
- No sensitive information exposed through friend codes
